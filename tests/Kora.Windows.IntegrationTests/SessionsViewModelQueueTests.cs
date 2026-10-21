using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Commands;
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
    public async Task NativePendingSubsetIsIndependentAndReviewCancelKeepsDraftThenExactConfirmRetainsUnselectedWorkAndClosureInvalidatesIds()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var action = new VersionAction();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access, action,
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance, queue);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var viewer = new SessionsViewModel(service, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink),
            access, f.Time, NullLogger<DurableEvidenceQuery>.Instance), access, NullLogger<SessionsViewModel>.Instance);
        try
        {
            await viewer.RefreshAsync();
            await viewer.SelectAsync(viewer.Sessions.Single());
            foreach (var profile in new[] { SessionQueueReadProfile.Version, SessionQueueReadProfile.Capabilities,
                SessionQueueReadProfile.Readiness, SessionQueueReadProfile.Runtimes })
            {
                var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
                await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueEnqueue, snapshot.SessionId.Value,
                    snapshot.Generation.Value)
                {
                    QueueRevision = snapshot.Revision, WorkRequestId = Guid.NewGuid(), TaskId = Guid.NewGuid(), QueueProfile = profile,
                }, RequestOrigin.LocalUi, () => true, f.Token);
            }
            await viewer.RefreshWorkAsync();
            var original = viewer.QueueRecords.ToArray();
            viewer.NameDraft = "unchanged draft";
            viewer.SelectWorkRecord(viewer.WorkRecords.Single(row => row.TaskId == original[3].Request.TaskId.Value));
            var currentSelection = viewer.SelectedWorkRecord;
            viewer.SelectPendingCancellationEntries([original[2], original[0]]);
            viewer.SelectedWorkRecord.Should().BeSameAs(currentSelection);
            viewer.CanReviewPendingCancellation.Should().BeTrue();
            var count = f.Count("security_audit_events");
            await viewer.ReviewPendingCancellationAsync();
            viewer.CanConfirmPendingCancellation.Should().BeTrue();
            viewer.PendingCancellationStatus.Should().Contain("subset: 2").And.Contain("readiness.get")
                .And.Contain(original[0].Request.TaskId.Value.ToString("D")).And.Contain(original[0].ExpiresAt.ToString("O"));
            viewer.PendingCancellationStatus.IndexOf(original[0].Request.TaskId.Value.ToString("D"), StringComparison.Ordinal)
                .Should().BeLessThan(viewer.PendingCancellationStatus.IndexOf(original[2].Request.TaskId.Value.ToString("D"), StringComparison.Ordinal));
            await viewer.DiscardPendingCancellationAsync();
            viewer.CanReviewPendingCancellation.Should().BeTrue();
            viewer.CanConfirmPendingCancellation.Should().BeFalse();
            viewer.PendingCancellationStatus.Should().Contain("Selected 2");
            viewer.NameDraft.Should().Be("unchanged draft");
            f.Count("security_audit_events").Should().Be(count);
            viewer.QueueRecords.Should().Equal(original);
            await viewer.ReviewPendingCancellationAsync();
            viewer.SelectPendingCancellationEntries([original[1]]);
            viewer.CanConfirmPendingCancellation.Should().BeFalse();
            await viewer.ConfirmPendingCancellationAsync();
            (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().Equal(original);
            await viewer.RefreshWorkAsync();
            viewer.PendingCancellationStatus.Should().Contain("Selected 0");
            viewer.SelectPendingCancellationEntries([original[0], original[2]]);
            await viewer.ReviewPendingCancellationAsync();
            await viewer.ConfirmPendingCancellationAsync();
            viewer.QueueRecords.Should().Equal(original[1], original[3]);
            viewer.WorkRecords.Where(row => row.Queue?.Entry.State == SessionQueueState.Cancelled).Should().HaveCount(2);
            viewer.PendingCancellationEntries.Should().Equal(original[1], original[3]);
            viewer.NameDraft.Should().Be("unchanged draft");
            action.Reads.Should().Be(0);
            viewer.SelectPendingCancellationEntries([original[1]]);
            await viewer.ReviewPendingCancellationAsync();
            viewer.SelectWorkRecord(viewer.WorkRecords[^1]);
            viewer.CanConfirmPendingCancellation.Should().BeFalse();
        }
        finally { viewer.Close(); }
        viewer.PendingCancellationEntries.Should().BeEmpty();
        viewer.PendingCancellationStatus.Should().NotContain("task ");
    }

    private sealed class VersionAction : IDeterministicVersionQueueAction
    {
        internal Action? BeforeObserve { get; set; }
        internal int Reads { get; private set; }
        public CapabilityReply Observe(SessionQueueReadProfile profile, CancellationToken token, SessionQueueCapabilityTarget? target = null)
        {
            Reads++;
            BeforeObserve?.Invoke();
            return new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0.0", "Fixed local read."));
        }
    }
}
