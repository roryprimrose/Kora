using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed class SessionsViewModelQueueTests
{
    [WindowsFact]
    public async Task Native_queue_uses_the_same_exact_revisions_workflow_without_selection_dispatch_or_late_privacy_resume()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new VersionAction();
        await using var queue = new SessionQueueService(fixture.Store, fixture.Store, new(fixture.Tasks), access, action,
            new(), NullLogger<SessionQueueService>.Instance, fixture.Time);
        var service = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
            NullLogger<SessionWorkspaceService>.Instance, queue);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(service, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink),
            access, fixture.Time, NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance);
        await viewer.RefreshAsync();
        await viewer.SelectAsync(viewer.Sessions.Single());
        action.Reads.Should().Be(0);
        viewer.CanControlQueue.Should().BeFalse();
        await viewer.ReadQueueAsync();
        viewer.CanControlQueue.Should().BeTrue();
        await viewer.EnqueueVersionAsync();
        var first = viewer.QueueRecords.Single();
        action.Reads.Should().Be(0);
        viewer.SelectQueueEntry(first);
        viewer.CanCancelQueueEntry.Should().BeTrue();
        await viewer.CancelQueueEntryAsync();
        viewer.QueueRecords.Should().BeEmpty();
        (await fixture.Store.ReadQueueEntryAsync(first.Request.SessionId, first.Request.TaskId, fixture.Token))!
            .State.Should().Be(SessionQueueState.Cancelled);
        await viewer.EnqueueVersionAsync();
        await viewer.ClearQueueAsync();
        viewer.QueueRecords.Should().BeEmpty();
        await viewer.EnqueueVersionAsync();
        await viewer.DispatchQueueAsync();
        action.Reads.Should().Be(1);
        viewer.QueueRecords.Should().BeEmpty();
        viewer.Detail.Should().Contain("1.0.0");
        await viewer.EnqueueVersionAsync();
        access.ControlRevision++;
        await viewer.DispatchQueueAsync();
        action.Reads.Should().Be(1, "renewed private admission must not resume eligibility captured before its epoch changed");
        viewer.QueueRecords.Should().ContainSingle();
        viewer.Close();
        viewer.QueueRecords.Should().BeEmpty();
        viewer.CanControlQueue.Should().BeFalse();
    }

    private sealed class VersionAction : IDeterministicVersionQueueAction
    {
        internal int Reads { get; private set; }
        public CapabilityReply Observe(CancellationToken token)
        {
            Reads++;
            return new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0.0", "Fixed local read."));
        }
    }
}
