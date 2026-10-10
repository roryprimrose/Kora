using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class ExactGrantsViewModelTests
{
    [Fact]
    public async Task PassiveNativeListAndCurrentInspectionRequireNoIntentOrExecutionAndDisplayTruthfulSafeMetadata()
    {
        await using var f = new Fixture();
        f.State.RefreshCommand.CanExecute(null).Should().BeTrue();
        f.State.Reviewed = true;
        f.State.CanRevoke.Should().BeFalse();
        await f.State.RefreshCommand.ExecuteAsync();
        f.State.Records.Should().ContainSingle().Which.Should().Be(f.Preview);
        f.State.Message.Should().Contain("No more");
        f.State.Selected = f.Preview;
        f.State.Selected = f.Preview;
        f.State.InspectCommand.CanExecute(null).Should().BeTrue();
        await f.State.InspectCommand.ExecuteAsync();
        f.State.Inspection.Should().Contain(f.Preview.Grant.Id.Value.ToString("D"));
        f.State.Inspection.Should().Contain("Current applicability and running-work status are unknown");
        f.State.Inspection.Should().Contain("Definition SHA-256");
        f.State.Inspection.Should().Contain("not recorded");
        f.State.CanRevoke.Should().BeFalse();
        f.State.Reviewed = true;
        f.State.CanRevoke.Should().BeTrue();
        f.Eligible = false;
        f.State.CanRevoke.Should().BeFalse();
        f.Eligible = true;
        f.Access.CanControl = false;
        f.State.CanRevoke.Should().BeFalse();
        f.Access.CanControl = true;
        f.Intents.Tasks.Should().BeEmpty();
        f.State.RevokeCommand.CanExecute(null).Should().BeTrue();
        await f.State.RevokeCommand.ExecuteAsync();
        f.State.Message.Should().Contain("committed and read back").And.Contain("unknown");
        f.Store.Current!.Grant.Status.Should().Be(OperationGrantStatus.Revoked);
        f.Store.RevokeRequest!.SessionId.Should().NotBe(f.Preview.Grant.ApprovedProposal.Request.SessionId);
        f.Store.Audit!.ApprovalId.Should().Be(f.Preview.Grant.Id.Value);
        f.Store.CapturedAdmission!().Should().BeFalse();
        f.State.CanRevoke.Should().BeFalse();
        await f.State.RefreshAsync(f.Token);
        f.State.Selected = f.Store.Current;
        await f.State.InspectAsync(f.Token);
        f.State.Inspection.Should().Contain("explicit-user-revocation");
        f.State.Reviewed = true;
        f.State.CanRevoke.Should().BeFalse();
        await f.State.RefreshAsync(f.Token);
        f.State.Inspection.Should().Contain("Select one");
        f.State.InspectCommand.CanExecute(null).Should().BeFalse();
        f.State.NextCommand.CanExecute(null).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task OriginObservationsAndUseMetadataAreDisplayedWithoutInferringApplicability(int observation)
    {
        await using var f = new Fixture();
        var session = observation == 0 ? null : new WorkSessionAuthorization(
            f.Preview.Grant.ApprovedProposal.Request.SessionId, new(2), observation == 1);
        f.Store.Current = f.Preview with
        {
            OriginSession = session, OriginSessionRemoved = observation == 3,
            Grant = f.Preview.Grant with { UseCount = 1, LastUsedAt = DateTimeOffset.UnixEpoch },
        };
        await f.InspectAsync();
        f.State.Inspection.Should().Contain(observation switch { 0 => "unavailable", 1 => "Active", 2 => "Done", _ => "removed" });
        f.State.Inspection.Should().Contain("Use count: 1").And.Contain("1970");
        f.State.Reviewed = true;
        f.State.Selected = null;
        f.State.CanRevoke.Should().BeFalse();
        f.State.Inspection.Should().Contain("Select one");
    }

    [Fact]
    public async Task PaginationConflictAndMissingCurrentRecordAreExplicit()
    {
        await using var f = new Fixture();
        f.Store.Next = new("store", 1, new string('a', 64), f.Preview.Grant.Id.Value);
        await f.State.RefreshAsync(f.Token);
        f.State.NextCommand.CanExecute(null).Should().BeTrue();
        f.State.Message.Should().Contain("Next");
        await f.State.NextCommand.ExecuteAsync();
        f.Store.Next = null;
        await f.State.NextAsync(f.Token);
        var absentNext = () => f.State.NextAsync(f.Token);
        await absentNext.Should().ThrowAsync<InvalidOperationException>();
        await f.InspectAsync();
        f.Store.Conflict = true;
        f.State.Reviewed = true;
        await f.State.RevokeAsync(f.Token);
        f.State.Message.Should().Contain("stale");
        f.Store.Current = null;
        f.State.Selected = f.Preview;
        await f.State.InspectAsync(f.Token);
        f.State.Message.Should().Contain("unavailable");
        var noReview = () => f.State.RevokeAsync(f.Token);
        await noReview.Should().ThrowAsync<InvalidOperationException>();
        f.State.Selected = null;
        var noSelection = () => f.State.InspectAsync(f.Token);
        await noSelection.Should().ThrowAsync<InvalidOperationException>();
        f.State.Busy.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ReadsRejectSelectionHostPrivacyRevisionAndLateCloseChanges(int change)
    {
        await using var f = new Fixture();
        f.Store.BeforePage = () =>
        {
            if (change == 0) { f.State.Selected = f.Preview; }
            else if (change == 1) { f.Eligible = false; }
            else if (change == 2) { f.Access.ControlRevision++; }
            else { f.State.Dispose(); }
        };
        var read = () => f.State.RefreshAsync(f.Token);
        await read.Should().ThrowAsync<Exception>();
        f.State.Records.Should().BeEmpty();
        f.State.Busy.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task InspectDiscardsChangedSelectionPrivacyOwnershipAndReadCancellation(int change)
    {
        await using var f = new Fixture();
        await f.State.RefreshAsync(f.Token);
        f.State.Selected = f.Preview;
        using var cancellation = new CancellationTokenSource();
        f.Store.BeforeRead = () =>
        {
            if (change == 0) { f.State.Selected = null; }
            else if (change == 1) { f.Access.CanInspect = false; }
            else if (change == 2) { f.Eligible = false; }
            else { cancellation.Cancel(); }
        };
        var inspect = () => f.State.InspectAsync(cancellation.Token);
        await inspect.Should().ThrowAsync<Exception>();
        f.State.Inspection.Should().Contain("Select one");
        f.State.Reviewed.Should().BeFalse();
    }

    [Fact]
    public async Task BusyCancelledDisposedAndDeniedAdmissionNeverStartAnotherReadOrMutation()
    {
        await using var f = new Fixture();
        f.Eligible = false;
        var read = () => f.State.RefreshAsync(f.Token);
        await read.Should().ThrowAsync<InvalidOperationException>();
        f.Eligible = true;
        f.Access.CanInspect = false;
        await read.Should().ThrowAsync<InvalidOperationException>();
        f.Access.CanInspect = true;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => f.State.RefreshAsync(cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        var pending = new TaskCompletionSource<ExactGrantPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingPage = pending.Task;
        var running = f.State.RefreshAsync(f.Token);
        await read.Should().ThrowAsync<InvalidOperationException>();
        f.State.RefreshCommand.CanExecute(null).Should().BeFalse();
        f.State.Dispose();
        f.State.Dispose();
        f.State.CanRevoke.Should().BeFalse();
        pending.SetResult(new([f.Preview], null));
        var late = () => running;
        await late.Should().ThrowAsync<OperationCanceledException>();
        await read.Should().ThrowAsync<ObjectDisposedException>();
        f.State.ReportFailure(new IOException("safe test failure"));
        f.State.Message.Should().Contain("Closed");
        f.State.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ExactRevocationRevalidatesControlSelectionReviewAndPrivacyAtCommit(int change)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.State.Reviewed = true;
        f.Store.BeforeRevoke = () =>
        {
            if (change == 0) { f.Access.CanControl = false; }
            else if (change == 1) { f.State.Selected = null; }
            else if (change == 2) { f.State.Reviewed = false; }
            else if (change == 3) { f.Eligible = false; }
            else { f.Access.ControlRevision++; }
        };
        var revoke = () => f.State.RevokeAsync(f.Token);
        await revoke.Should().ThrowAsync<InvalidOperationException>();
        f.Store.Revokes.Should().Be(0);
        f.Store.Current!.Grant.Status.Should().Be(OperationGrantStatus.Active);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task PostCommitReadbackLossIsUncertainRecoveryNeverFalseRollbackOrLateSuccess(int failure)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.State.Reviewed = true;
        f.Store.AfterRevoke = () =>
        {
            if (failure == 0) { f.Eligible = false; }
            else if (failure == 1) { f.Store.Current = null; }
            else if (failure == 2) { f.Store.ReadFailure = new OperationCanceledException(); }
            else { f.Store.BeforeRead = () => f.State.Reviewed = false; }
        };
        var revoke = () => f.State.RevokeAsync(f.Token);
        await revoke.Should().ThrowAsync<IOException>();
        f.Store.Revokes.Should().Be(1);
        f.State.ReportFailure(new IOException());
        f.State.Message.Should().Contain("No success or rollback");
        f.State.Records.Should().BeEmpty();
        f.State.Reviewed.Should().BeFalse();
        f.Store.AfterRevoke = null;
        f.Store.BeforeRead = null;
        f.Store.ReadFailure = null;
        f.Eligible = true;
        var retry = () => f.Control.RevokeAsync(f.Preview, static () => true, f.Token);
        await retry.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task StorageMalformedAndCancelledMutationsHaveExplicitFailureHandling(int failure)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.State.Reviewed = true;
        f.Store.RevokeFailure = failure switch { 0 => new IOException(), 1 => new InvalidDataException(), _ => new OperationCanceledException() };
        var revoke = () => f.State.RevokeCommand.ExecuteAsync();
        await revoke.Should().ThrowAsync<Exception>();
        f.State.ReportFailure(f.Store.RevokeFailure!);
        f.State.Message.Should().Contain("No success or rollback");
        f.State.RevokeCommand.CanExecute(null).Should().BeFalse();
        f.Store.Revokes.Should().Be(0);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task AmbientUserModelOrHostSystemRequestsCannotSupplyNativeControlAuthority(RequestOrigin origin)
    {
        await using var f = new Fixture();
        using var ambient = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var revoke = () => f.Control.RevokeAsync(f.Preview, static () => true, f.Token);
        await revoke.Should().ThrowAsync<InvalidOperationException>();
        f.Intents.Tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task HostAuthorizationRejectsMissingPersistenceWrongOriginAndForeignHostRequest()
    {
        await using var f = new Fixture();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var unsupported = new HostAuthorizationService(f.Tracing.Store, TimeProvider.System);
        var rejected = () => unsupported.RevokeExactAsync(request, new(1), f.Preview, static () => true, f.Token).AsTask();
        await rejected.Should().ThrowAsync<InvalidOperationException>();
        var wrongOrigin = () => f.Authorization.RevokeExactAsync(HostRequest.Create(RequestOrigin.HostSystem), new(1), f.Preview, static () => true, f.Token).AsTask();
        await wrongOrigin.Should().ThrowAsync<InvalidOperationException>();
        var foreign = () => f.Authorization.RevokeExactAsync(request with { }, new(1), f.Preview, static () => true, f.Token).AsTask();
        await foreign.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task LateReceiptAfterTaskOutcomeCannotPublishARevokeSuccess()
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.State.Reviewed = true;
        f.Intents.BeforeCommit = record =>
        {
            if (record.IsTerminal && f.Store.Revokes == 1) { f.Access.CanControl = false; }
        };
        var revoke = () => f.State.RevokeAsync(f.Token);
        await revoke.Should().ThrowAsync<InvalidOperationException>();
        f.State.Message.Should().NotContain("committed and read back");
    }

    [Fact]
    public async Task ChangedControlEpochCannotReuseAnInspectionEvenAfterOwnershipAppearsAvailableAgain()
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.State.Reviewed = true;
        f.Access.ControlRevision++;
        f.State.CanRevoke.Should().BeFalse();
        var stale = () => f.State.RevokeAsync(f.Token);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        f.Store.Revokes.Should().Be(0);
        await f.State.InspectAsync(f.Token);
        f.State.Reviewed.Should().BeFalse();
        f.State.Reviewed = true;
        f.State.CanRevoke.Should().BeTrue();
    }

    [Fact]
    public async Task FailureDiagnosticsContainOnlyFailureTypeAndNoPrivateExceptionBody()
    {
        var logger = new EnabledLogger();
        await using var f = new Fixture(logger);
        f.State.ReportFailure(new IOException("private-sensitive-body"));
        logger.Last.Should().Contain(nameof(IOException)).And.NotContain("private-sensitive-body");
        logger.Context.Should().NotBeNull();
        logger.Context!.Request.Origin.Should().Be(RequestOrigin.LocalUi);
        logger.Context.Outcome.Should().Be(HostOperationOutcome.Failed);
        HostActivity.Current.Should().BeNull();
    }

    private sealed class EnabledLogger : ILogger<ExactGrantsViewModel>
    {
        internal string? Last { get; private set; }
        internal HostActivity? Context { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Context = HostActivity.RequireCurrent();
            Last = formatter(state, exception);
        }
    }

    private sealed class Access : ISessionWorkspaceAccess
    {
        public bool CanInspect { get; set; } = true;
        public bool CanControl { get; set; } = true;
        public long ControlRevision { get; set; } = 1;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal InteractionFixture Tracing { get; } = new();
        internal AudioControlTestStore Intents { get; } = new();
        internal Access Access { get; } = new();
        internal bool Eligible { get; set; } = true;
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal ExactGrantInspection Preview { get; }
        internal ExactGrantTestStore Store { get; }
        internal HostAuthorizationService Authorization { get; }
        internal ExactGrantControlAdmission Control { get; }
        internal ExactGrantsViewModel State { get; }
        internal Fixture(ILogger<ExactGrantsViewModel>? logger = null)
        {
            var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), RequestOrigin.LocalUi, new(Guid.NewGuid()));
            var grant = new OperationGrant(new(Guid.NewGuid()), new(1),
                new(request, new(Guid.NewGuid()), new(1), InteractionFixture.Binding(), HostOperationEffect.BoundedRead, DateTimeOffset.UnixEpoch),
                OperationGrantScope.Perpetual, new(1), RequestOrigin.LocalUi, DateTimeOffset.UnixEpoch);
            Preview = new("store", grant, new(request.SessionId, new(1), true), false);
            Store = new(Intents, Preview);
            Authorization = new(Store, TimeProvider.System);
            Control = new(Intents, Store, new(Intents), Authorization);
            State = new(Store, Control, Access, () => Eligible, logger ?? NullLogger<ExactGrantsViewModel>.Instance);
        }
        internal async Task InspectAsync()
        {
            await State.RefreshAsync(Token);
            State.Selected = Store.Current;
            await State.InspectAsync(Token);
        }
        public async ValueTask DisposeAsync()
        {
            State.Dispose();
            await Control.DisposeAsync();
            Tracing.Dispose();
        }
    }
}
