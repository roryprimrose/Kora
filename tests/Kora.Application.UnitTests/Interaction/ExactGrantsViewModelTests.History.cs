using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class ExactGrantsViewModelTests
{
    [Fact]
    public async Task ExplicitHistoryReadsRetireSingleReviewAndNeverCreateMutationOrOriginSessionAuthority()
    {
        await using var f = new Fixture();
        f.State.ReadHistoryCommand.CanExecute(null).Should().BeFalse();
        var uninspected = () => f.State.ReadHistoryAsync(f.Token);
        await uninspected.Should().ThrowAsync<InvalidOperationException>();
        var noNext = () => f.State.NextHistoryAsync(f.Token);
        await noNext.Should().ThrowAsync<InvalidOperationException>();
        await f.InspectAsync();
        f.State.CanReadHistory.Should().BeTrue();
        f.State.Reviewed = true;
        f.Store.HistoryResult = History(f, next: "cursor");
        await f.State.ReadHistoryCommand.ExecuteAsync();
        f.State.Reviewed.Should().BeFalse();
        f.State.CanRevoke.Should().BeFalse();
        f.State.HistoryRows.Should().HaveCount(2);
        f.State.HistoryRows[0].Should().Contain("Requested (not committed change)")
            .And.Contain("Committed exact grant change").And.Contain("Original question ID/revision")
            .And.Contain("not viewer authority").And.Contain("ExpiredButPresent");
        f.State.HistoryRows[1].Should().Contain("not separately retained");
        f.State.HistorySummary.Should().Contain("finite audit ceiling").And.Contain("scanned rows: 4096")
            .And.Contain("more prefix").And.Contain("not forensic").And.Contain("not reconstructed");
        f.State.NextHistoryCommand.CanExecute(null).Should().BeTrue();
        var viewer = f.Store.HistoryViewer;
        viewer!.SessionId.Should().NotBe(f.Preview.Grant.ApprovedProposal.Request.SessionId);
        f.Store.HistoryAdmission!().Should().BeFalse();
        f.Store.HistoryResult = History(f);
        await f.State.NextHistoryCommand.ExecuteAsync();
        f.State.SynchronizeAuthority();
        f.Store.HistoryViewer.Should().BeSameAs(viewer);
        f.State.HistorySummary.Should().Contain("none in this snapshot");
        f.State.NextHistoryCommand.CanExecute(null).Should().BeFalse();
        f.Store.Revokes.Should().Be(0);
        await f.State.InspectAsync(f.Token);
        f.State.HistoryRows.Should().BeEmpty();
        f.State.Reviewed = true;
        f.State.CanRevoke.Should().BeTrue();
    }

    [Fact]
    public async Task HistoryCanInspectWithoutControlAndCannotReactivateExplicitBatchReview()
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.State.SetBatchSelection([f.Preview]);
        await f.State.PreviewBatchAsync(f.Token);
        f.State.BatchReviewed = true;
        await f.State.InspectAsync(f.Token);
        f.Access.CanControl = false;
        f.State.CanReadHistory.Should().BeTrue();
        await f.State.ReadHistoryAsync(f.Token);
        f.State.BatchReviewed.Should().BeFalse();
        f.State.CanRevokeBatch.Should().BeFalse();
        f.Store.Revokes.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task LateExactHistoryRowsAreSuppressedForCloseSelectionPrivacyOwnershipEpochAndCancellation(int change)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        using var cancellation = new CancellationTokenSource();
        f.Store.HistoryResult = History(f);
        f.Store.BeforeHistory = () =>
        {
            if (change == 0) { f.State.Dispose(); }
            else if (change == 1) { f.State.Selected = null; }
            else if (change == 2) { f.Eligible = false; }
            else if (change == 3) { f.Access.CanInspect = false; }
            else if (change == 4) { f.Access.ControlRevision++; }
            else { cancellation.Cancel(); }
        };
        var read = () => f.State.ReadHistoryAsync(cancellation.Token);
        await read.Should().ThrowAsync<Exception>();
        f.State.HistoryRows.Should().BeEmpty();
        f.State.CanReadHistory.Should().BeFalse();
        f.State.Reviewed.Should().BeFalse();
        f.Store.HistoryAdmission!().Should().BeFalse();
        f.Store.Revokes.Should().Be(0);
    }

    [Fact]
    public async Task CloseCancelsHeldAsyncReadAndDropsLateReturnedContent()
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        var pending = new TaskCompletionSource<ExactGrantHistoryPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingHistory = pending.Task;
        var operation = f.State.ReadHistoryAsync(f.Token);
        f.State.Dispose();
        pending.SetResult(History(f));
        var result = () => operation;
        await result.Should().ThrowAsync<OperationCanceledException>();
        f.State.HistoryRows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PreviouslyHeldHistoryIsClearedOnCurrentAuthorityLoss(int change)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.Store.HistoryResult = History(f);
        await f.State.ReadHistoryAsync(f.Token);
        if (change == 0) { f.Access.ControlRevision++; }
        else if (change == 1) { f.Eligible = false; }
        else { f.Access.CanInspect = false; }
        f.State.SynchronizeAuthority();
        f.State.HistoryRows.Should().BeEmpty();
        f.State.CanReadHistory.Should().BeFalse();
        f.State.CanRevoke.Should().BeFalse();
    }

    [Fact]
    public async Task RecordAbsenceIsExplicitUnavailableNotEmptySuccessOrRecreatedAuthority()
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.Store.Current = null;
        await f.State.ReadHistoryAsync(f.Token);
        f.State.HistorySummary.Should().Contain("unavailable").And.Contain("No grant");
        f.State.CanReadHistory.Should().BeFalse();
        f.State.CanRevoke.Should().BeFalse();
        f.State.HistoryRows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ForeignAndOversizedHistoryFailsClosedWithTypedPrivateFreeFailureSurface(int change)
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        var page = History(f);
        if (change == 0) { page = page with { Current = f.Preview with { StoreIdentity = "foreign" } }; }
        else if (change == 1) { page = page with { Current = f.Preview with { Grant = f.Preview.Grant with { Id = new(Guid.NewGuid()) } } }; }
        else if (change == 2) { page = page with { Records = Enumerable.Repeat(page.Records[0], 51).ToImmutableArray() }; }
        else { page = page with { Next = new string('x', 66000) }; }
        f.Store.HistoryResult = page;
        f.State.ReadHistoryCommand.Execute(null);
        f.State.Message.Should().Contain("unavailable").And.Contain(nameof(InvalidDataException));
        f.State.HistoryRows.Should().BeEmpty();
        f.State.Busy.Should().BeFalse();
    }

    [Fact]
    public async Task FailedHistoryReadHasNoEmptySuccessOrPrivateFailureBody()
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.Store.ReadFailure = new IOException("private-user-provider-body");
        f.State.ReadHistoryCommand.Execute(null);
        f.State.Message.Should().Contain("unavailable").And.Contain(nameof(IOException)).And.NotContain("private-user-provider-body");
        f.State.HistoryRows.Should().BeEmpty();
        f.State.CanRevoke.Should().BeFalse();
    }

    [Fact]
    public async Task AmbientModelOrHistoricalRequestCannotMintAnOriginalNativeHistoryViewer()
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        using var ambient = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request);
        var read = () => f.State.ReadHistoryAsync(f.Token);
        await read.Should().ThrowAsync<InvalidOperationException>();
        f.Store.HistoryViewer.Should().BeNull();
    }

    private static ExactGrantHistoryPage History(Fixture f, string? next = null)
    {
        var request = f.Preview.Grant.ApprovedProposal.Request;
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "approval.revoke-exact-batch", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.LocalUser,
            request.TaskId.Value.ToString("D"));
        var change = new AuthorityAuditChange("grant", f.Preview.Grant.Id.Value.ToString("D"), 1, new string('a', 64));
        var provenance = new AuthorityAuditProvenance(8, "security_audit_events", 1, new string('a', 64),
            new string('b', 32), new string('c', 16), new(1), new(1), HostInteractionOutcome.Revoked,
            new(Guid.NewGuid()), new(1), f.Preview.Grant.Id, new(1), [change],
            audit.WithOutcome(SecurityAuditOutcome.Requested), new(1), [change]);
        var row = new ExactGrantHistoryEntry(request, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1),
            EvidenceSegmentStatus.ExpiredButPresent, audit, provenance, 1, 1);
        return new(f.Preview, [row, row with
            {
                Request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin),
                Provenance = provenance with
                {
                    RequestedAudit = null, RequestedGrants = null, RequestedGrantRevision = null,
                    QuestionId = null, QuestionRevision = null,
                },
            }],
            next, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, 4096, true);
    }
}
