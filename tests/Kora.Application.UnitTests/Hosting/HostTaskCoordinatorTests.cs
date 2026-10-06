using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

[Collection("Host tracing")]
public sealed class HostTaskCoordinatorTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public HostTaskCoordinatorTests()
    {
        HostActivity.ConfigureW3C();
        ActivitySource.AddActivityListener(listener);
    }

    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Identified_intent_dispatch_and_receipt_are_ordered_by_revision()
    {
        var store = new RecordingStore();
        var coordinator = new HostTaskCoordinator(store);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var intent = await coordinator.RecordIntentAsync(request, CancellationToken.None);
        var dispatched = await coordinator.RecordDispatchAsync(intent, CancellationToken.None);
        var terminal = await coordinator.RecordOutcomeAsync(dispatched, HostTaskState.Succeeded, CancellationToken.None);
        terminal.IsTerminal.Should().BeTrue();
        store.Commits.Select(item => item.State).Should().Equal(
            HostTaskState.IntentRecorded, HostTaskState.DispatchRecorded, HostTaskState.Succeeded);
        store.Commits.Select(item => item.Revision.Value).Should().Equal(1, 2, 3);
        store.Commits.Should().OnlyContain(item => item.Request == request);
    }

    [Fact]
    public async Task Failed_or_cancelled_intent_cannot_produce_a_dispatchable_record()
    {
        var store = new RecordingStore { FailCommit = true };
        var coordinator = new HostTaskCoordinator(store);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var action = async () => await coordinator.RecordIntentAsync(request, CancellationToken.None);
        await action.Should().ThrowAsync<IOException>();
        store.Commits.Should().BeEmpty();
        store.FailCommit = false;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancellation = async () => await coordinator.RecordIntentAsync(request, cancelled.Token);
        await cancellation.Should().ThrowAsync<OperationCanceledException>();
        store.Commits.Should().BeEmpty();
    }

    [Fact]
    public async Task Recovery_commits_interrupted_and_unknown_without_action_callbacks()
    {
        var store = new RecordingStore();
        var coordinator = new HostTaskCoordinator(store);
        await store.CommitAsync(new(HostRequest.Create(RequestOrigin.LocalUi), new(1), HostTaskState.IntentRecorded), 0, CancellationToken.None);
        var intent = new HostTaskRecord(HostRequest.Create(RequestOrigin.ActivatedVoice), new(1), HostTaskState.IntentRecorded);
        await store.CommitAsync(intent, 0, CancellationToken.None);
        await store.CommitAsync(intent.Next(HostTaskState.DispatchRecorded), 1, CancellationToken.None);

        var recovered = await coordinator.RecoverAsync(100, CancellationToken.None);
        recovered.Select(item => item.State).Should().Equal(HostTaskState.Interrupted, HostTaskState.Unknown);
        (await coordinator.RecoverAsync(100, CancellationToken.None)).Should().BeEmpty();
        store.Commits.Count(item => item.State == HostTaskState.DispatchRecorded).Should().Be(1);
    }

    [Fact]
    public async Task Stale_revision_and_receipt_failure_preserve_unknown_recovery()
    {
        var store = new RecordingStore();
        var coordinator = new HostTaskCoordinator(store);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var intent = await coordinator.RecordIntentAsync(request, CancellationToken.None);
        var dispatched = await coordinator.RecordDispatchAsync(intent, CancellationToken.None);
        var stale = async () => await coordinator.RecordDispatchAsync(intent, CancellationToken.None);
        await stale.Should().ThrowAsync<InvalidOperationException>();
        store.FailCommit = true;
        var receipt = async () => await coordinator.RecordOutcomeAsync(dispatched, HostTaskState.Succeeded, CancellationToken.None);
        await receipt.Should().ThrowAsync<IOException>();
        store.FailCommit = false;
        (await coordinator.RecoverAsync(1, CancellationToken.None)).Should().ContainSingle()
            .Which.State.Should().Be(HostTaskState.Unknown);
    }

    [Fact]
    public async Task Recovery_rejects_invalid_limits_and_corrupt_query_results()
    {
        var store = new RecordingStore();
        var coordinator = new HostTaskCoordinator(store);
        var limit = async () => await coordinator.RecoverAsync(101, CancellationToken.None);
        await limit.Should().ThrowAsync<ArgumentOutOfRangeException>();
        store.CorruptRead = true;
        var corrupt = async () => await coordinator.RecoverAsync(1, CancellationToken.None);
        await corrupt.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Missing_or_cross_session_context_fails_before_intent_commit()
    {
        var store = new RecordingStore();
        var coordinator = new HostTaskCoordinator(store);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var missing = async () => await coordinator.RecordIntentAsync(request, CancellationToken.None);
        await missing.Should().ThrowAsync<InvalidOperationException>();
        using var other = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        await missing.Should().ThrowAsync<InvalidOperationException>();
        store.Commits.Should().BeEmpty();
    }

    [Fact]
    public async Task Nonterminal_receipts_and_oversized_recovery_results_are_rejected()
    {
        var store = new RecordingStore();
        var coordinator = new HostTaskCoordinator(store);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var intent = await coordinator.RecordIntentAsync(request, CancellationToken.None);
        var nonterminal = async () => await coordinator.RecordOutcomeAsync(intent, HostTaskState.DispatchRecorded, CancellationToken.None);
        await nonterminal.Should().ThrowAsync<InvalidOperationException>();
        store.OversizedRead = true;
        var recovery = async () => await coordinator.RecoverAsync(1, CancellationToken.None);
        await recovery.Should().ThrowAsync<InvalidDataException>();
    }

    private sealed class RecordingStore : IHostTaskStore
    {
        public List<HostTaskRecord> Commits { get; } = [];
        public bool FailCommit { get; set; }
        public bool CorruptRead { get; set; }
        public bool OversizedRead { get; set; }
        private readonly Dictionary<HostId<TaskIdentity>, HostTaskRecord> records = [];

        public ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailCommit)
            {
                throw new IOException("Synthetic commit failure.");
            }
            var actual = records.GetValueOrDefault(record.Request.TaskId)?.Revision.Value ?? 0;
            if (actual != expectedRevision || record.Revision.Value != actual + 1)
            {
                throw new InvalidOperationException("The task revision changed.");
            }
            records[record.Request.TaskId] = record;
            Commits.Add(record);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<HostTaskRecord> result = OversizedRead
                ? [new(HostRequest.Create(RequestOrigin.HostSystem), new(1), HostTaskState.IntentRecorded),
                   new(HostRequest.Create(RequestOrigin.HostSystem), new(1), HostTaskState.IntentRecorded)]
                : CorruptRead
                ? [new(HostRequest.Create(RequestOrigin.HostSystem), new(1), HostTaskState.Succeeded)]
                : records.Values.Where(record => !record.IsTerminal).Take(limit).ToArray();
            return ValueTask.FromResult(result);
        }
    }
}
