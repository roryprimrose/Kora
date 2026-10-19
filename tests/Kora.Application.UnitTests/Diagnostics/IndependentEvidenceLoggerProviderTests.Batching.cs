using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Kora.Application.Auditing;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Diagnostics;

public sealed partial class IndependentEvidenceLoggerProviderTests
{
    [Fact]
    public async Task Renderer_audit_timing_separates_completed_copy_from_projection_gate_wait()
    {
        var phases = new ConcurrentQueue<PresenceSwitchPhase>();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timing = new MeterListener();
        var instrument = PresenceSwitchTiming.Instrument;
        timing.InstrumentPublished = (published, observer) =>
        {
            if (ReferenceEquals(published, instrument)) { observer.EnableMeasurementEvents(published); }
        };
        timing.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (tags[0].Value is not PresenceSwitchPhase phase) { return; }
            phases.Enqueue(phase);
            if (phase == PresenceSwitchPhase.AuditProjectionGateWaiting) { waiting.TrySetResult(); }
        });
        timing.Start();
        using var copy = new Sink("copy");
        using var projection = new Sink("projection", blocked: true);
        await using var provider = Create(copy, projection);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            AppearanceOptionRegistry.Get(AppearanceOption.NativePresenceRenderer).AuditAction,
            SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "preferences.device-local");
        Task writing = Task.CompletedTask;
        try
        {
            Log(provider.CreateLogger("audit-timing-fixture"), 0);
            await projection.Started.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            await copy.NextDeliveryAsync();
            phases.Should().BeEmpty("ordinary projections do not emit required-audit timings");
            writing = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>())
                .WriteAsync(audit, TestContext.Current.CancellationToken);
            await waiting.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            phases.Should().ContainInOrder(PresenceSwitchPhase.AuditCopyGateWaiting,
                PresenceSwitchPhase.AuditCopyGateEntered, PresenceSwitchPhase.AuditCopyReturned,
                PresenceSwitchPhase.AuditProjectionGateWaiting);
            phases.Should().NotContain(PresenceSwitchPhase.AuditProjectionGateEntered);
            writing.IsCompleted.Should().BeFalse();
        }
        finally
        {
            projection.Release.Set();
            await writing.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        }
        phases.Should().ContainInOrder(PresenceSwitchPhase.AuditProjectionGateWaiting,
            PresenceSwitchPhase.AuditProjectionGateEntered, PresenceSwitchPhase.AuditProjectionReturned);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task In_flight_batches_keep_the_original_record_bound_and_exact_overload_gaps(bool activities)
    {
        using var copy = new Sink("copy");
        using var storage = new Sink("projection");
        using var releaseFirst = new ManualResetEventSlim();
        using var releaseSecond = new ManualResetEventSlim();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var projection = new BatchedSink(storage)
        {
            BeforeBatch = ordinal =>
            {
                if (ordinal == 1) { first.SetResult(); releaseFirst.Wait(TestContext.Current.CancellationToken); }
                if (ordinal == 2) { second.SetResult(); releaseSecond.Wait(TestContext.Current.CancellationToken); }
            },
        };
        await using var provider = new EvidenceLoggerProvider([copy, projection], copy,
            bufferDiagnostics: true, requiredOrdinaryCopy: copy);
        var logger = provider.CreateLogger("batch-fixture");
        void Emit(int index)
        {
            if (activities) { CompleteActivity(); }
            else { Log(logger, index); }
        }
        try
        {
            Emit(0);
            await first.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            await copy.NextDeliveryAsync();
            for (var index = 1; index < 65; index++) { Emit(index); await copy.NextDeliveryAsync(); }
            releaseFirst.Set();
            await second.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            projection.BatchSizes.Take(2).Should().Equal(1, IBatchedEvidenceSink.MaximumBatchSize);
            for (var index = 65; index < 1065; index++) { Emit(index); await copy.NextDeliveryAsync(); }
            using var cancellation = new CancellationTokenSource();
            var cancelling = provider.FlushAsync(cancellation.Token);
            cancellation.Cancel();
            var observeCancellation = async () => await cancelling;
            await observeCancellation.Should().ThrowAsync<OperationCanceledException>();
            var flushing = provider.FlushAsync(TestContext.Current.CancellationToken);
            flushing.IsCompleted.Should().BeFalse();
            releaseSecond.Set();
            await flushing.WaitAsync(Deadline, TestContext.Current.CancellationToken);

            copy.Identities().Should().HaveCount(1065);
            storage.Identities().Should().HaveCount(1026, "one record committed before the remaining 1025 slots filled");
            projection.BatchSizes.Should().OnlyContain(count => count >= 1 && count <= IBatchedEvidenceSink.MaximumBatchSize);
            projection.BatchSizes.Sum().Should().Be(1026);
            var gaps = copy.Gaps.Where(gap => gap.Reason == EvidenceGapReason.ProjectionBackpressure).ToArray();
            gaps.Should().HaveCount(39);
            gaps.Select(gap => gap.EvidenceId!.Value).Should().BeEquivalentTo(copy.Identities().Except(storage.Identities()));
            storage.PersistenceHadHostContext.Should().BeFalse();
        }
        finally { releaseFirst.Set(); releaseSecond.Set(); }
    }

    [Fact]
    public async Task Flush_barrier_finishes_without_waiting_for_a_later_batch()
    {
        using var copy = new Sink("copy");
        using var storage = new Sink("projection");
        using var releaseFirst = new ManualResetEventSlim();
        using var releaseLast = new ManualResetEventSlim();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var last = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var projection = new BatchedSink(storage)
        {
            BeforeBatch = ordinal =>
            {
                if (ordinal == 1) { first.SetResult(); releaseFirst.Wait(TestContext.Current.CancellationToken); }
                if (ordinal == 3) { last.SetResult(); releaseLast.Wait(TestContext.Current.CancellationToken); }
            },
        };
        await using var provider = new EvidenceLoggerProvider([copy, projection], copy,
            bufferDiagnostics: true, requiredOrdinaryCopy: copy);
        var logger = provider.CreateLogger("batch-barrier-fixture");
        try
        {
            Log(logger, 0);
            await first.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            await copy.NextDeliveryAsync();
            for (var index = 1; index <= 32; index++) { Log(logger, index); await copy.NextDeliveryAsync(); }
            var flushing = provider.FlushAsync(TestContext.Current.CancellationToken);
            Log(logger, 33);
            await copy.NextDeliveryAsync();
            releaseFirst.Set();
            await last.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            await flushing.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            storage.Diagnostics.Should().HaveCount(33);
            projection.BatchSizes.Should().Equal(1, 32, 1);
        }
        finally { releaseFirst.Set(); releaseLast.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_batch_preparation_cannot_silently_omit_or_reidentify_a_projection(bool missing)
    {
        using var copy = new Sink("copy");
        using var storage = new Sink("projection");
        var projection = new BatchedSink(storage) { MissingPreparation = missing, WrongIdentity = !missing };
        await using var provider = new EvidenceLoggerProvider([copy, projection], copy,
            bufferDiagnostics: true, requiredOrdinaryCopy: copy);
        var write = () => Log(provider.CreateLogger("invalid-batch-fixture"), 1);
        write.Should().Throw<IOException>();
        copy.Gaps.Should().ContainSingle().Which.Reason.Should().Be(EvidenceGapReason.SinkFailure);
        storage.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_batches_report_every_accepted_identity_and_join_both_lanes()
    {
        using var copy = new Sink("copy");
        using var storage = new Sink("projection");
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var projection = new BatchedSink(storage)
        {
            FailBatches = true,
            BeforeBatch = ordinal =>
            {
                if (ordinal == 1) { started.SetResult(); release.Wait(TestContext.Current.CancellationToken); }
            },
        };
        var provider = new EvidenceLoggerProvider([copy, projection], copy,
            bufferDiagnostics: true, requiredOrdinaryCopy: copy);
        try
        {
            var logger = provider.CreateLogger("batch-failure-fixture");
            for (var index = 0; index < 64; index++)
            {
                Log(logger, index);
                if (index == 0) { await started.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken); }
                await copy.NextDeliveryAsync();
            }
        }
        finally
        {
            release.Set();
            var disposing = async () => await provider.DisposeAsync();
            await disposing.Should().ThrowAsync<IOException>();
        }
        copy.Diagnostics.Should().HaveCount(64);
        storage.Diagnostics.Should().BeEmpty();
        projection.BatchSizes.Sum().Should().Be(64);
        var gaps = copy.Gaps.Where(gap => gap.Reason == EvidenceGapReason.SinkFailure).ToArray();
        gaps.Should().HaveCount(64).And.OnlyContain(gap => gap.Sink == projection.Name);
        gaps.Select(gap => gap.EvidenceId!.Value).Should().BeEquivalentTo(copy.Identities());
    }

    [Fact]
    public async Task Waiting_required_delivery_precedes_the_next_ordinary_delivery()
    {
        var gate = new EvidenceDeliveryGate();
        using var releaseFirst = new ManualResetEventSlim();
        using var releaseAudit = new ManualResetEventSlim();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var auditEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new ConcurrentQueue<string>();
        var first = Task.Run(() =>
        {
            using var lease = gate.Enter(required: false);
            firstEntered.SetResult();
            releaseFirst.Wait(TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);
        var audit = Task.CompletedTask;
        var next = Task.CompletedTask;
        try
        {
            await firstEntered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            audit = Task.Run(() =>
            {
                using var lease = gate.Enter(required: true);
                order.Enqueue("audit");
                auditEntered.SetResult();
                releaseAudit.Wait(TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken);
            SpinWait.SpinUntil(() => gate.WaitingRequired == 1, Deadline).Should().BeTrue();
            next = Task.Run(() =>
            {
                using var lease = gate.Enter(required: false);
                order.Enqueue("ordinary");
            }, TestContext.Current.CancellationToken);
            releaseFirst.Set();
            await auditEntered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            next.IsCompleted.Should().BeFalse();
        }
        finally
        {
            releaseFirst.Set();
            releaseAudit.Set();
            await Task.WhenAll(first, audit, next).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        }
        order.Should().Equal("audit", "ordinary");
    }

    private sealed class BatchedSink(Sink storage) : IBatchedEvidenceSink
    {
        public string Name => storage.Name;
        public ConcurrentQueue<int> BatchSizes { get; } = new();
        public Action<int>? BeforeBatch { get; init; }
        public bool FailBatches { get; init; }
        public bool MissingPreparation { get; init; }
        public bool WrongIdentity { get; init; }
        public void WriteAudit(AuditEnvelope envelope) => storage.WriteAudit(envelope);
        public void WriteDiagnostic(DiagnosticEnvelope envelope) => PrepareDiagnosticWrite(envelope).Write();
        public void WriteActivity(CompletedActivityEnvelope envelope) => PrepareActivityWrite(envelope).Write();
        public Action PrepareDiagnostic(DiagnosticEnvelope envelope) => PrepareDiagnosticWrite(envelope).Write;
        public Action PrepareActivity(CompletedActivityEnvelope envelope) => PrepareActivityWrite(envelope).Write;
        public IPreparedEvidenceWrite PrepareDiagnosticWrite(DiagnosticEnvelope envelope)
        {
            if (MissingPreparation) { return null!; } // Deliberately violates the sink contract to verify fail-closed admission.
            return new Prepared(WrongIdentity ? new(Guid.NewGuid()) : envelope.EvidenceId, storage.PrepareDiagnostic(envelope));
        }
        public IPreparedEvidenceWrite PrepareActivityWrite(CompletedActivityEnvelope envelope) =>
            new Prepared(envelope.EvidenceId, storage.PrepareActivity(envelope));
        public void WriteBatch(IReadOnlyList<IPreparedEvidenceWrite> writes)
        {
            BatchSizes.Enqueue(writes.Count);
            BeforeBatch?.Invoke(BatchSizes.Count);
            if (FailBatches) { throw new IOException("Synthetic atomic batch failure."); }
            foreach (var write in writes) { write.Write(); }
        }
        private sealed class Prepared(HostId<EvidenceIdentity> identity, Action write) : IPreparedEvidenceWrite
        {
            public HostId<EvidenceIdentity> EvidenceId => identity;
            public void Write() => write();
        }
    }
}
