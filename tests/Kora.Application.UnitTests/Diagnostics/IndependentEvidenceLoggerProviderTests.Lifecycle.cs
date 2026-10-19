using System.Diagnostics.Metrics;
using AwesomeAssertions;
using Kora.Application.Auditing;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Diagnostics;

public sealed partial class IndependentEvidenceLoggerProviderTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Application_timing_rejects_unregistered_phases_before_metric_callbacks(int value)
    {
        var measurements = 0;
        using var timing = new MeterListener();
        var instrument = PresenceSwitchTiming.Instrument;
        timing.InstrumentPublished = (published, observer) =>
        {
            if (ReferenceEquals(published, instrument)) { observer.EnableMeasurementEvents(published); }
        };
        timing.SetMeasurementEventCallback<long>((_, _, _, _) => Interlocked.Increment(ref measurements));
        timing.Start();
        var mark = () => PresenceSwitchTiming.Mark((PresenceSwitchPhase)value);
        mark.Should().Throw<ArgumentOutOfRangeException>();
        Volatile.Read(ref measurements).Should().Be(0);
    }

    [Fact]
    public async Task A_flush_barrier_consumes_queue_capacity_without_consuming_a_record_slot()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var writer = new BufferedEvidenceWriter();
        var delivered = 0;
        Task flushing = Task.CompletedTask;
        try
        {
            writer.Enqueue(() =>
            {
                entered.SetResult();
                release.Wait(TestContext.Current.CancellationToken);
                Interlocked.Increment(ref delivered);
            });
            await entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            for (var index = 0; index < 1023; index++)
            {
                writer.TryEnqueue(() => Interlocked.Increment(ref delivered)).Should().BeTrue();
            }
            flushing = writer.FlushAsync(TestContext.Current.CancellationToken);
            writer.TryEnqueue(() => Interlocked.Increment(ref delivered)).Should().BeFalse();
            writer.RequireHealthy();
        }
        finally { release.Set(); }
        await flushing.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Volatile.Read(ref delivered).Should().Be(1024);
    }

    [Fact]
    public async Task Closed_writer_rejects_records_and_flush_barriers_without_accepting_work()
    {
        var writer = new BufferedEvidenceWriter();
        await writer.DisposeAsync();
        var enqueue = () => writer.TryEnqueue(static () => throw new InvalidOperationException("Must not run."));
        enqueue.Should().Throw<IOException>().WithMessage("*closed*");
        var flush = () => writer.FlushAsync(TestContext.Current.CancellationToken);
        await flush.Should().ThrowAsync<IOException>().WithMessage("*closed*barrier*");
        var batch = () => writer.TryEnqueue(new LifecyclePrepared());
        batch.Should().Throw<InvalidOperationException>().WithMessage("*does not support*");
    }

    [Fact]
    public void Delivery_gate_retains_nested_ownership_and_rejects_a_duplicate_exit()
    {
        var gate = new EvidenceDeliveryGate();
        var outer = gate.Enter(required: false);
        using (gate.Enter(required: true))
        {
            using (gate.Enter(required: false)) { gate.WaitingRequired.Should().Be(0); }
        }
        outer.Dispose();
        var duplicate = outer.Dispose;
        duplicate.Should().Throw<SynchronizationLockException>();
        using var fresh = gate.Enter(required: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_projection_preparation_keeps_the_required_copy_and_explicit_gap(bool activities)
    {
        using var copy = new Sink("copy");
        using var projection = new Sink("projection") { PreparationFailure = new DiagnosticRetentionUnavailableException() };
        await using var provider = Create(copy, projection);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        if (activities) { CompleteActivity(); }
        else { Log(provider.CreateLogger("unavailable-preparation"), 0); }
        await provider.FlushAsync(TestContext.Current.CancellationToken);
        copy.Identities().Should().ContainSingle();
        projection.Identities().Should().BeEmpty();
        copy.Gaps.Should().ContainSingle().Which.EvidenceId.Should().Be(copy.Identities()[0]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Projection_batches_distinguish_unavailable_policy_from_storage_and_gap_failures(
        bool storageFailure, bool gapFailure)
    {
        using var copy = new Sink("copy") { FailGaps = gapFailure };
        using var storage = new Sink("projection");
        var projection = new BatchedSink(storage)
        {
            BatchFailure = storageFailure ? new StorageAdmissionException() : new DiagnosticRetentionUnavailableException(),
        };
        var provider = new EvidenceLoggerProvider([copy, projection], copy,
            bufferDiagnostics: true, requiredOrdinaryCopy: copy);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        Log(provider.CreateLogger("unavailable-batch"), 0);
        var dispose = async () => await provider.DisposeAsync();
        if (storageFailure || gapFailure) { await dispose.Should().ThrowAsync<IOException>(); }
        else { await dispose(); }
        copy.Diagnostics.Should().ContainSingle();
        storage.Diagnostics.Should().BeEmpty();
        if (!gapFailure)
        {
            copy.Gaps.Should().ContainSingle().Which.Reason.Should().Be(storageFailure
                ? EvidenceGapReason.StorageNotAdmitted : EvidenceGapReason.SinkFailure);
        }
    }

    [Fact]
    public async Task Timed_renderer_audit_with_strict_sinks_does_not_claim_independent_lane_phases()
    {
        var phases = new List<PresenceSwitchPhase>();
        using var timing = new MeterListener();
        var instrument = PresenceSwitchTiming.Instrument;
        timing.InstrumentPublished = (published, observer) =>
        {
            if (ReferenceEquals(published, instrument)) { observer.EnableMeasurementEvents(published); }
        };
        timing.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (tags[0].Value is PresenceSwitchPhase phase) { phases.Add(phase); }
        });
        timing.Start();
        using var copy = new Sink("copy");
        using var projection = new Sink("projection");
        await using var provider = new EvidenceLoggerProvider([copy, projection], copy);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            AppearanceOptionRegistry.Get(AppearanceOption.NativePresenceRenderer).AuditAction,
            SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "preferences.device-local");
        await new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>())
            .WriteAsync(audit, TestContext.Current.CancellationToken);
        copy.Audits.Should().ContainSingle();
        projection.Audits.Should().ContainSingle();
        phases.Should().BeEmpty();
    }

    private sealed class LifecyclePrepared : IPreparedEvidenceWrite
    {
        public HostId<EvidenceIdentity> EvidenceId { get; } = new(Guid.NewGuid());
        public void Write() => throw new InvalidOperationException("Unsupported batches must never execute.");
    }
}
