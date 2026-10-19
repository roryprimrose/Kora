using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionQueueTests
{
    [WindowsFact]
    public async Task DiagnosticsPrivateSqliteSequentialAuditFailureRetainsFirstCommitAndReportsExactUncertainSecondWithoutRetry()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = await queue.ReviewDiagnosticsPlanAsync(snapshot,
            [SessionQueueReadProfile.Version, SessionQueueReadProfile.Readiness, SessionQueueReadProfile.Runtimes],
            SessionDiagnosticsPlanPrerequisite.None, RequestOrigin.LocalUi, () => true, f.Token);
        var controls = new HashSet<HostId<TaskIdentity>>();
        checkpoint.Audit = (_, _) =>
        {
            controls.Add(HostActivity.RequireCurrent().Request.TaskId);
            if (controls.Count == 2) { throw new IOException("Second sequential control required audit unavailable."); }
        };
        var result = await queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        checkpoint.Audit = null;
        result.Items.Select(item => item.State).Should().Equal(SessionDiagnosticsPlanItemState.Accepted,
            SessionDiagnosticsPlanItemState.Unavailable, SessionDiagnosticsPlanItemState.NotAttempted);
        var rows = (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries;
        rows.Should().ContainSingle();
        var durable = rows.Single();
        durable.Should().Be(result.Items[0].CommittedEntry);
        durable.Dependency.Should().BeNull();
        durable.IsPending.Should().BeTrue();
        result.Items[1].Request.Should().NotBeNull();
        result.Items[1].CommittedEntry.Should().BeNull();
        result.Items[2].Request.Should().BeNull();
        var status = await f.Store.ReadQueueEntryAsync(durable.Request.SessionId, durable.Request.TaskId, f.Token);
        status.Should().Be(durable);
        var retry = () => queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        await retry.Should().ThrowAsync<InvalidOperationException>();
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().Equal(durable);
    }

    [WindowsFact]
    public async Task DiagnosticsReviewKeepsPauseAndOriginalExpiredDeadlinesAndRequiresWholePlanCapacity()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var original = await EnqueueAsync(f, f.Request.SessionId);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access, new NeverAction(),
            new(pendingPerSession: 2), Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionQueueService>.Instance, f.Time);
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        await queue.ExecuteCommandAsync(PauseCommand(snapshot, true), RequestOrigin.LocalUi, () => true, f.Token);
        f.Time.Now = original.ExpiresAt;
        snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        snapshot.Entries.Single().State.Should().Be(SessionQueueState.Expired);
        var tooMany = () => queue.ReviewDiagnosticsPlanAsync(snapshot,
            [SessionQueueReadProfile.Version, SessionQueueReadProfile.Readiness], SessionDiagnosticsPlanPrerequisite.None,
            RequestOrigin.LocalUi, () => true, f.Token);
        await tooMany.Should().ThrowAsync<InvalidOperationException>();
        var review = await queue.ReviewDiagnosticsPlanAsync(snapshot, [SessionQueueReadProfile.Readiness],
            SessionDiagnosticsPlanPrerequisite.None, RequestOrigin.LocalUi, () => true, f.Token);
        review.ExistingPending.Should().Be(1);
        review.ResultingPending.Should().Be(2);
        var result = await queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        result.Items.Single().State.Should().Be(SessionDiagnosticsPlanItemState.Accepted);
        var after = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        after.DispatchControl.Should().Be(snapshot.DispatchControl);
        after.DispatchControl!.Paused.Should().BeTrue();
        after.Entries[0].Should().Be(original with { State = SessionQueueState.Expired });
        after.Entries[1].ExpiresAt.Should().Be(f.Time.Now.AddMinutes(30));
        after.Entries[1].Dependency.Should().BeNull();
        (await f.Store.FindReadyAsync(access.ControlRevision, new(), f.Token)).Should().BeNull();
    }

    [WindowsFact]
    public async Task DiagnosticsConcurrentPauseChangesControlWithoutQueueRevisionAndPreventsEveryPlannedEffect()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = await queue.ReviewDiagnosticsPlanAsync(snapshot, [SessionQueueReadProfile.Version],
            SessionDiagnosticsPlanPrerequisite.None, RequestOrigin.LocalUi, () => true, f.Token);
        var paused = await queue.ExecuteCommandAsync(PauseCommand(snapshot, true), RequestOrigin.LocalUi, () => true, f.Token);
        paused.Queue!.Revision.Should().Be(snapshot.Revision);
        var result = await queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        result.Items.Single().State.Should().Be(SessionDiagnosticsPlanItemState.Conflict);
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().BeEmpty();
    }

    [WindowsFact]
    public async Task DiagnosticsSessionRetirementBeforeConfirmationIsConflictNotImplicitResumeOrEmptySuccess()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        await using var queue = PauseQueue(f, new(), new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var review = await queue.ReviewDiagnosticsPlanAsync(snapshot, [SessionQueueReadProfile.Version],
            SessionDiagnosticsPlanPrerequisite.None, RequestOrigin.LocalUi, () => true, f.Token);
        await WindowsSqliteSessionWorkspaceTests.Service(f, new()).ChangeLifecycleAsync(f.Request.SessionId, snapshot.Generation,
            false, RequestOrigin.LocalUi, f.Token);
        var result = await queue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, () => true, f.Token);
        result.Items.Single().State.Should().Be(SessionDiagnosticsPlanItemState.Conflict);
        (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token)).Authority.IsActive.Should().BeFalse();
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().BeEmpty();
    }
}
