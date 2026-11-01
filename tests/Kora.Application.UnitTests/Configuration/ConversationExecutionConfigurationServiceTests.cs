using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class ConversationExecutionConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ConversationExecutionConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task OriginalRoutesReviewConfirmPersistAndRestartWithoutDispatch(RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        await using var f = new Fixture();
        f.Service.Current.Should().BeNull();
        f.Service.Description.Should().Contain("unavailable");
        f.Service.Observe();
        f.Service.Current!.Limit.Should().Be(ConversationExecutionLimit.Default);
        f.Service.Current.Saved.Should().BeFalse();
        var original = f.Service.Capture();
        f.Service.Observe();
        f.Service.Capture().Should().BeSameAs(original);
        await f.Service.RefreshAsync(origin, () => f.Eligible, Token);
        var candidate = await f.Review(1, origin);
        candidate.Description.Should().Contain("2 -> 1").And.Contain("does not qualify capacity two");
        f.Service.Review.Should().BeSameAs(candidate);
        f.Preferences.Writes.Should().Be(0);
        (await f.Service.ConfirmAsync(candidate, origin, initiator, f.Policy, () => f.Eligible, Token)).Should().BeTrue();
        f.Service.Current!.Limit.Value.Should().Be(1);
        f.Service.Current.Saved.Should().BeTrue();
        f.Service.Description.Should().Contain("not qualified effective capacity");
        f.Service.Review.Should().BeNull();
        var restarted = new ConversationExecutionConfigurationService(f.Preferences, f.Admission, f.Audit);
        restarted.Observe();
        restarted.Current!.Limit.Value.Should().Be(1);
        f.Preferences.Pending.Should().BeFalse();
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => item.Initiator == initiator
            && string.Equals(item.ActionId, "configuration.conversation-execution-maximum", StringComparison.Ordinal));
        f.Store.Tasks.Should().OnlyContain(task => task.Request.Origin == origin);
        var cancelled = await f.Review(2, origin);
        f.Service.CancelReview();
        await f.Service.Invoking(service => service.ConfirmAsync(cancelled, origin, initiator, f.Policy, () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(1);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("model")]
    [InlineData("voice")]
    [InlineData("review")]
    [InlineData("refresh")]
    [InlineData("saved")]
    [InlineData("host")]
    [InlineData("session")]
    public async Task StaleOrNonoriginalReviewCannotWrite(string boundary)
    {
        await using var f = new Fixture();
        var candidate = await f.Review(1);
        if (boundary is "review") { _ = await f.Review(2); }
        if (boundary is "refresh") { f.Service.HoldUnavailable(); f.Service.Observe(); }
        if (boundary is "saved") { f.Preferences.Value = new(1); }
        if (boundary is "host") { f.Eligible = false; }
        if (boundary is "session") { f.Store.Authority = f.Store.Authority! with { Generation = new(2) }; }
        await f.Service.Invoking(service => service.ConfirmAsync(candidate,
            boundary is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi,
            boundary is "model" ? SecurityAuditInitiator.ModelSuggestion : boundary is "voice"
                ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser, f.Policy, () => f.Eligible, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ProtectedCallDeniesVoiceWhileNativeRemainsAvailable()
    {
        await using var f = new Fixture();
        f.Call.State = CallState.Active;
        var candidate = await f.Review(1, RequestOrigin.ActivatedVoice);
        (await f.Service.ConfirmAsync(candidate, RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand,
            f.Policy, () => true, Token)).Should().BeFalse();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        await f.Set(1);
        f.Service.Current!.Limit.Value.Should().Be(1);
    }

    [Theory]
    [InlineData("load")]
    [InlineData("requested")]
    [InlineData("audit-drift")]
    [InlineData("save")]
    [InlineData("access")]
    [InlineData("readback")]
    [InlineData("terminal")]
    [InlineData("receipt")]
    [InlineData("late-host")]
    [InlineData("late-readback")]
    [InlineData("confirm")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-load")]
    public async Task FailedEvidenceNeverActivatesDemandOrSilentlyRecovers(string boundary)
    {
        await using var f = new Fixture();
        var candidate = await f.Review(1);
        f.Preferences.LoadFailure = boundary is "load" ? new InvalidDataException() : null;
        f.Preferences.Failure = boundary is "save" ? new IOException()
            : boundary is "access" ? new UnauthorizedAccessException() : null;
        f.Preferences.AfterSave = () => { if (boundary is "readback") { f.Preferences.Value = new(2); } };
        f.Audit.BeforeWrite = item =>
        {
            if (boundary is "requested" && item.Outcome == SecurityAuditOutcome.Requested
                || boundary is "terminal" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
            if (boundary is "audit-drift" && item.Outcome == SecurityAuditOutcome.Requested) { f.Preferences.Value = new(2); }
        };
        f.Store.FailTerminal = boundary is "receipt";
        f.Store.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (boundary is "late-host") { f.Eligible = false; }
            if (boundary is "late-readback") { f.Preferences.Value = new(2); }
        };
        f.Preferences.ConfirmFailure = boundary is "confirm" ? new IOException() : null;
        f.Preferences.AfterConfirm = () =>
        {
            if (boundary is "confirmed-readback") { f.Preferences.Value = new(2); }
            if (boundary is "confirmed-load") { f.Preferences.LoadFailure = new IOException(); }
        };
        await f.Service.Invoking(service => service.ConfirmAsync(candidate, RequestOrigin.LocalUi,
            SecurityAuditInitiator.LocalUser, f.Policy, () => f.Eligible, Token)).Should().ThrowAsync<Exception>();
        f.Service.Current.Should().BeNull();
        f.Service.Capture().Should().BeNull();
        f.Service.Review.Should().BeNull();
        f.Preferences.LoadFailure = new InvalidDataException();
        f.Service.Invoking(service => service.Observe()).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task PersistedDriftInvalidatesCaptureAndReviewReceiptRevalidatesOriginalAuthority()
    {
        await using var f = new Fixture();
        f.Service.Observe();
        f.Preferences.Value = new(1);
        f.Service.Invoking(service => service.Capture()).Should().Throw<InvalidDataException>();
        f.Service.Current.Should().BeNull();
        _ = await f.Review(2);
        f.Store.BeforeCommit = task => { if (task.State == HostTaskState.Succeeded) { f.Eligible = false; } };
        await f.Invoking(value => value.Review(2)).Should().ThrowAsync<InvalidOperationException>();
        f.Service.Review.Should().BeNull();
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ReentrantObservationCannotActivateACommittingPreference()
    {
        await using var f = new Fixture();
        _ = await f.Review(1);
        f.Audit.BeforeWrite = entry =>
        {
            if (entry.Outcome == SecurityAuditOutcome.Requested)
            { f.Service.Invoking(service => service.Observe()).Should().Throw<InvalidOperationException>(); }
        };
        await f.Set(1);
        f.Service.Current!.Limit.Value.Should().Be(1);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        internal Fixture()
        {
            Admission = new(Store, Store, new(Store));
            Policy = new(Call);
            Service = new(Preferences, Admission, Audit);
        }
        internal bool Eligible { get; set; } = true;
        internal Preferences Preferences { get; } = new();
        internal ConversationalReplyConfigurationServiceTests.Audit Audit { get; } = new();
        internal ConversationalReplyConfigurationServiceTests.Call Call { get; } = new();
        internal AudioControlTestStore Store { get; } = new();
        internal AudioControlAdmission Admission { get; }
        internal CallCommunicationPolicy Policy { get; }
        internal ConversationExecutionConfigurationService Service { get; }
        internal Task<ConversationExecutionReview> Review(int maximum, RequestOrigin origin = RequestOrigin.LocalUi) =>
            Service.ReviewAsync(new(maximum), Policy.Current.Revision, origin, () => Eligible, Token);
        internal async Task Set(int maximum)
        {
            var candidate = await Review(maximum);
            (await Service.ConfirmAsync(candidate, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser, Policy, () => Eligible, Token))
                .Should().BeTrue();
        }
        public async ValueTask DisposeAsync() { Policy.Dispose(); await Admission.DisposeAsync(); }
    }

    internal sealed class Preferences : IConversationExecutionPreferences
    {
        internal ConversationExecutionLimit? Value { get; set; }
        internal bool Pending { get; private set; }
        internal int Writes { get; private set; }
        internal Exception? Failure { get; set; }
        internal Exception? LoadFailure { get; set; }
        internal Exception? ConfirmFailure { get; set; }
        internal Action? AfterSave { get; set; }
        internal Action? AfterConfirm { get; set; }
        public ConversationExecutionLimit? Load() => LoadFailure is { } failure ? throw failure
            : Pending ? throw new InvalidDataException() : Value;
        public ConversationExecutionLimit? ReadBack() => Value;
        public void BeginWrite() => Pending = true;
        public void Save(ConversationExecutionLimit limit)
        {
            if (Failure is { } failure) { throw failure; }
            Value = limit;
            Writes++;
            AfterSave?.Invoke();
        }
        public void ConfirmWrite()
        {
            if (ConfirmFailure is { } failure) { throw failure; }
            Pending = false;
            AfterConfirm?.Invoke();
        }
    }
}
