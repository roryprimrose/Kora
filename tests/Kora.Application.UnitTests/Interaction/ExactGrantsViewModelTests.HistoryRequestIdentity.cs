using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class ExactGrantsViewModelTests
{
    [Fact]
    public async Task CommittedRequestFiltersComposeWithoutAdoptingViewerOrOriginalGrantAuthorityAndClearOnDisposal()
    {
        var logger = new EnabledLogger();
        await using var f = new Fixture(logger);
        await f.InspectAsync();
        var target = f.Preview;
        var page = History(f, "next");
        var committed = HostRequest.Create(RequestOrigin.HostSystem);
        var row = page.Records[0] with { Request = committed };
        f.Store.HistoryResult = page with { Records = [row] };
        await f.State.ReadHistoryAsync(f.Token);
        var priorAdmission = f.Store.HistoryAdmissionId;
        var notifications = new List<string?>();
        f.State.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        var values = new[] { committed.SessionId.Value, committed.RequestId.Value, committed.TaskId.Value };
        for (var field = 0; field < values.Length; field++)
        {
            EditHistoryRequestIdentity(f.State, field, values[field].ToString("D"));
            EditHistoryRequestIdentity(f.State, field, values[field].ToString("D"));
            AssertHistoryFilterRetired(f);
            f.State.CanReadHistory.Should().BeTrue();
            f.State.Inspection.Should().NotContain(target.Grant.Id.Value.ToString("D"));
            f.Store.HistoryReads.Should().Be(1);
        }
        f.State.HistoryOutcomeFilter = row.Audit.Outcome.ToString();
        f.State.HistoryInitiatorFilter = row.Audit.Initiator.ToString();
        f.State.HistoryRetentionFilter = row.Retention.ToString();
        f.State.HistoryCommittedFromUtc = f.State.HistoryCommittedUntilUtc = row.CommittedAt.ToString("O");
        f.State.HistoryDueFromUtc = f.State.HistoryDueUntilUtc = row.DueAt.ToString("O");
        f.State.HistoryAuditCorrelationId = row.Audit.CorrelationId.ToString("D");
        f.State.HistoryApprovalFilter = "Absent";
        await f.State.ReadHistoryCommand.ExecuteAsync();
        f.Store.HistoryQuery.Should().Be(new ExactGrantHistoryQuery(row.Audit.Outcome, initiator: row.Audit.Initiator,
            retention: row.Retention, committedFromUtc: row.CommittedAt, committedUntilUtc: row.CommittedAt,
            dueFromUtc: row.DueAt, dueUntilUtc: row.DueAt, auditCorrelationId: row.Audit.CorrelationId,
            approval: ExactGrantHistoryApproval.Absent, sessionId: committed.SessionId.Value,
            requestId: committed.RequestId.Value, taskId: committed.TaskId.Value));
        f.Store.HistoryExact.Should().BeSameAs(target);
        f.Store.HistoryAdmissionId.Should().NotBe(priorAdmission);
        f.Store.HistoryViewer!.SessionId.Should().NotBe(committed.SessionId);
        f.Store.HistoryViewer.RequestId.Should().NotBe(committed.RequestId);
        f.Store.HistoryViewer.TaskId.Should().NotBe(committed.TaskId);
        f.Store.HistoryViewer.Origin.Should().Be(RequestOrigin.LocalUi);
        f.State.HistoryRows.Should().ContainSingle();
        f.State.CanRevoke.Should().BeFalse();
        logger.Last.Should().BeNull();
        f.Store.Audit.Should().BeNull();
        f.Store.Revokes.Should().Be(0);
        f.Intents.Tasks.Should().BeEmpty();
        await f.State.ClearHistoryFiltersCommand.ExecuteAsync();
        AssertEmptyHistoryRequestIdentity(f.State);
        AssertHistoryFilterRetired(f);
        f.Store.HistoryReads.Should().Be(2);
        for (var field = 0; field < values.Length; field++)
        {
            EditHistoryRequestIdentity(f.State, field, values[field].ToString("D"));
        }
        notifications.Clear();
        f.State.Dispose();
        AssertEmptyHistoryRequestIdentity(f.State);
        notifications.Should().Contain(nameof(ExactGrantsViewModel.HistorySessionId))
            .And.Contain(nameof(ExactGrantsViewModel.HistoryRequestId)).And.Contain(nameof(ExactGrantsViewModel.HistoryTaskId));
        for (var field = 0; field < values.Length; field++)
        {
            var index = field;
            var edit = () => EditHistoryRequestIdentity(f.State, index, "late");
            edit.Should().Throw<ObjectDisposedException>();
        }
        HostActivity.Current.Should().BeNull();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task EachRequestIdentityEditCancelsHeldReadAndSuppressesLateRowsOrPrivateFailure(int field, bool fails)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        var pending = new TaskCompletionSource<ExactGrantHistoryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingHistory = pending.Task;
        var read = f.State.ReadHistoryCommand.ExecuteAsync();
        var admitted = f.Store.HistoryAdmission!;
        EditHistoryRequestIdentity(f.State, field, "private-superseded-id");
        f.Store.HistoryToken.IsCancellationRequested.Should().BeTrue();
        admitted().Should().BeFalse();
        if (fails) { pending.SetException(new IOException("private-superseded-id")); }
        else { pending.SetResult(History(f, "stale")); }
        await read;
        AssertHistoryFilterRetired(f);
        f.State.CanReadHistory.Should().BeTrue();
        f.State.Message.Should().NotContain("private-superseded-id");
        f.Store.HistoryReads.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidRequestIdentityIsExplicitPrivateFreeFailureWithoutStorageOrAuthorityFallback(int field)
    {
        var logger = new EnabledLogger();
        await using var f = new Fixture(logger);
        foreach (var invalid in new[]
        {
            " ", "Any", "00000000-0000-0000-0000-000000000000",
            "12345678-1234-1234-1234-123456789ABC", "12345678123412341234123456789abc",
            "{12345678-1234-1234-1234-123456789abc}", "12345678-1234-1234-1234-123456789ab",
            "12345678-1234-1234-1234-123456789abcd", " 12345678-1234-1234-1234-123456789abc",
            "12345678-1234-1234-1234-123456789abc ", new string('x', 4096),
        })
        {
            f.State.ClearHistoryFilters();
            await f.InspectAsync();
            EditHistoryRequestIdentity(f.State, field, invalid);
            f.State.ReadHistoryCommand.Execute(null);
            f.Store.HistoryReads.Should().Be(0);
            f.State.Message.Should().Contain("unavailable").And.Contain(nameof(InvalidDataException));
            logger.Last.Should().Contain(nameof(InvalidDataException));
            if (invalid.Length > 1)
            {
                f.State.Message.Should().NotContain(invalid);
                logger.Last.Should().NotContain(invalid);
            }
            f.State.CanReadHistory.Should().BeFalse();
            f.State.CanRevoke.Should().BeFalse();
        }
        f.Intents.Tasks.Should().BeEmpty();
        f.Store.Revokes.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ReaderCannotSubstituteViewerProposalOrNonmatchingRequestForCommittedIdentity(int field)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        var historical = HostRequest.Create(RequestOrigin.LocalUi);
        var value = field switch
        {
            0 => historical.SessionId.Value,
            1 => historical.RequestId.Value,
            _ => historical.TaskId.Value,
        };
        EditHistoryRequestIdentity(f.State, field, value.ToString("D"));
        f.Store.HistoryResult = History(f);
        var read = () => f.State.ReadHistoryAsync(f.Token);
        await read.Should().ThrowAsync<InvalidDataException>();
        f.State.HistoryRows.Should().BeEmpty();
        f.State.CanRevoke.Should().BeFalse();
        f.Store.Revokes.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task RequestFilteredReadSuppressesStaleCloseSelectionPrivacyOwnershipEpochCancellationAndSource(int change)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        var page = History(f);
        var request = page.Records[0].Request;
        f.State.HistorySessionId = request.SessionId.Value.ToString("D");
        f.State.HistoryRequestId = request.RequestId.Value.ToString("D");
        f.State.HistoryTaskId = request.TaskId.Value.ToString("D");
        f.Store.HistoryResult = page;
        using var cancellation = new CancellationTokenSource();
        f.Store.BeforeHistory = () =>
        {
            if (change == 0) { f.State.Dispose(); }
            else if (change == 1) { f.State.Selected = f.Preview; }
            else if (change == 2) { f.Eligible = false; }
            else if (change == 3) { f.Access.CanInspect = false; }
            else if (change == 4) { f.Access.ControlRevision++; }
            else if (change == 5) { cancellation.Cancel(); }
            else { f.Store.HistoryResult = page with { Current = f.Preview with { StoreIdentity = "replaced" } }; }
        };
        var read = () => f.State.ReadHistoryAsync(cancellation.Token);
        await read.Should().ThrowAsync<Exception>();
        f.State.HistoryRows.Should().BeEmpty();
        f.State.CanReadHistory.Should().BeFalse();
        f.State.CanRevoke.Should().BeFalse();
        f.Store.HistoryAdmission!().Should().BeFalse();
        f.Store.Revokes.Should().Be(0);
    }

    private static void EditHistoryRequestIdentity(ExactGrantsViewModel state, int field, string value)
    {
        if (field == 0) { state.HistorySessionId = value; }
        else if (field == 1) { state.HistoryRequestId = value; }
        else { state.HistoryTaskId = value; }
    }

    private static void AssertEmptyHistoryRequestIdentity(ExactGrantsViewModel state)
    {
        state.HistorySessionId.Should().BeEmpty();
        state.HistoryRequestId.Should().BeEmpty();
        state.HistoryTaskId.Should().BeEmpty();
    }
}
