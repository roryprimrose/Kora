using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionQueueTests
{
    [Theory]
    [InlineData(HostTaskState.IntentRecorded)]
    [InlineData(HostTaskState.DispatchRecorded)]
    [InlineData(HostTaskState.Succeeded)]
    [InlineData(HostTaskState.Failed)]
    [InlineData(HostTaskState.Cancelled)]
    [InlineData(HostTaskState.Unknown)]
    public async Task NativeRevisionGuardAdmitsExistingTaskStatesWithoutFreezingNormalSucceededDependencyPolicy(HostTaskState state)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        if (state != HostTaskState.IntentRecorded) { await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, state); }
        var source = (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token))!;
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var work = new HostRequest(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var queued = await ControlAsync(f, f.Request.SessionId, control => f.Store.EnqueueAsync(control, work, new(1),
            snapshot.Revision, 1, source.Request.TaskId, new(), () => true, f.Token,
            SessionQueueReadProfile.Readiness, source.Revision));
        var entry = queued.Entries.Single();
        entry.Dependency.Should().Be(source.Request.TaskId);
        entry.State.Should().Be(SessionQueueState.Pending);
        var tail = await EnqueueAsync(f, f.Request.SessionId);
        var ready = await f.Store.FindReadyAsync(1, new(), f.Token);
        if (state == HostTaskState.Succeeded) { ready.Should().Be(entry); }
        else { ready.Should().BeNull("an earlier blocked prerequisite is never skipped to dispatch the tail"); }
        if (state == HostTaskState.IntentRecorded)
        {
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
            (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().Be(entry,
                "the admission observation revision is not a durable permission or frozen state");
        }
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().Equal(entry, tail);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("revision")]
    [InlineData("cross-session")]
    [InlineData("guard-without-task")]
    public async Task GuardedEnqueueRejectsMissingOrChangedNativeMetadataAndLeavesTheQueueUntouched(string boundary)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var source = (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token))!;
        var session = f.Request.SessionId;
        if (boundary is "cross-session")
        {
            session = (await WindowsSqliteSessionWorkspaceTests.Service(f, new())
                .CreateAsync(new("Existing other session"), RequestOrigin.LocalUi, f.Token)).Authority.SessionId;
        }
        var work = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var snapshot = await f.Store.ReadQueueAsync(session, f.Token);
        HostId<TaskIdentity>? id = boundary is "guard-without-task" ? null
            : boundary is "missing" ? new(Guid.NewGuid()) : source.Request.TaskId;
        var expected = boundary is "revision" ? new HostRevision(source.Revision.Value + 1) : source.Revision;
        var audit = f.Count("security_audit_events");
        var refused = () => ControlAsync(f, session, control => f.Store.EnqueueAsync(control, work, snapshot.Generation,
            snapshot.Revision, 1, id, new(), () => true, f.Token, expectedDependencyRevision: expected));
        await refused.Should().ThrowAsync<InvalidOperationException>();
        (await f.Store.ReadQueueAsync(session, f.Token)).Entries.Should().BeEmpty();
        (await f.Tasks.ReadTaskAsync(work.TaskId, f.Token)).Should().BeNull();
        f.Count("security_audit_events").Should().Be(audit);
    }

    [Theory]
    [InlineData(SessionQueueReadProfile.Version, "version")]
    [InlineData(SessionQueueReadProfile.Capabilities, "capabilities.list")]
    [InlineData(SessionQueueReadProfile.Readiness, "readiness.get")]
    [InlineData(SessionQueueReadProfile.Runtimes, "runtime.list")]
    public async Task InternalPrerequisitesRetainExactReceiptsClocksAndCrossSessionContract(
        SessionQueueReadProfile profile, string token)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var source = (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token))!;
        var session = (await WindowsSqliteSessionWorkspaceTests.Service(f, new())
            .CreateAsync(new("Same original typed dependency contract"), RequestOrigin.LocalUi, f.Token)).Authority.SessionId;
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = PauseQueue(f, access, new NeverAction());
        var snapshot = await f.Store.ReadQueueAsync(session, f.Token);
        SessionQueueReadProfiles.Parse(token).Should().Be(profile);
        var command = new SessionCommand(SessionCommandOperation.QueueEnqueue, session.Value, 1)
        {
            QueueRevision = snapshot.Revision, WorkRequestId = Guid.NewGuid(), TaskId = Guid.NewGuid(),
            DependencyTaskId = source.Request.TaskId.Value, QueueProfile = profile,
        };
        command.ExpectedDependencyRevision.Should().BeNull();
        var receipt = await queue.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, f.Token);
        var entry = receipt.Queue!.Entries.Single();
        entry.Dependency.Should().Be(source.Request.TaskId);
        entry.ReadProfile.Should().Be(profile);
        entry.PendingLifetimeMinutes.Should().Be(30);
        entry.ExpiresAt.Should().Be(entry.EnqueuedAt.AddMinutes(30));
        entry.RecordVersion.Should().Be(profile == SessionQueueReadProfile.Version ? 2 : 4);
        entry.ActiveDeadlineAt.Should().BeNull();
        entry.Result.Should().BeNull();
        (await f.Store.ReadQueueEntryAsync(session, entry.Request.TaskId, f.Token))!.Should().Be(entry);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().Be(entry);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().BeEmpty();
    }
}
