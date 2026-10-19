using System.Diagnostics;
using System.Collections.ObjectModel;
using Kora.Application.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Diagnostics;

public sealed class EvidenceLoggerProvider : ILoggerProvider, ISupportExternalScope, IAsyncDisposable
{
    private readonly IEvidenceSink[] sinks;
    private readonly IEvidenceGapReporter gaps;
    private readonly TimeProvider clock;
    private readonly ActivityListener listener;
    private readonly BufferedEvidenceWriter? buffer;
    private readonly IndependentDelivery? independent;
    private readonly BufferedEvidenceWriter[] buffers = [];
    private readonly Lock captureGate = new();
    private readonly Dictionary<IEvidenceSink, EvidenceDeliveryGate> deliveryGates = new(ReferenceEqualityComparer.Instance);
    private readonly TaskCompletionSource capturesStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int captures;
    private bool disposed;
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    private sealed record IndependentDelivery(IDeferredEvidenceSink Copy, IEvidenceSink Projection,
        BufferedEvidenceWriter CopyWriter, BufferedEvidenceWriter ProjectionWriter);

    public EvidenceLoggerProvider(IEnumerable<IEvidenceSink> sinks, IEvidenceGapReporter gaps,
        TimeProvider? clock = null, bool bufferDiagnostics = false,
        IDeferredEvidenceSink? requiredOrdinaryCopy = null)
    {
        this.sinks = sinks.ToArray();
        this.gaps = gaps;
        this.clock = clock ?? TimeProvider.System;
        if (requiredOrdinaryCopy is not null && (!bufferDiagnostics || this.sinks.Length != 2
            || this.sinks.Count(sink => ReferenceEquals(sink, requiredOrdinaryCopy)) != 1
            || !ReferenceEquals(gaps, requiredOrdinaryCopy)))
        {
            throw new ArgumentException("Independent delivery requires two buffered sinks and an admitted required copy that also reports gaps.",
                nameof(requiredOrdinaryCopy));
        }
        foreach (var sink in this.sinks) { deliveryGates.TryAdd(sink, new EvidenceDeliveryGate()); }
        if (bufferDiagnostics)
        {
            if (this.sinks.Any(sink => sink is not IDeferredEvidenceSink))
            {
                throw new ArgumentException("Buffered sinks must support call-time evidence admission.", nameof(sinks));
            }
            if (requiredOrdinaryCopy is null)
            {
                buffer = new BufferedEvidenceWriter();
                buffers = [buffer];
            }
            else
            {
                var projection = this.sinks.Single(sink => !ReferenceEquals(sink, requiredOrdinaryCopy));
                independent = new(requiredOrdinaryCopy, projection,
                    new BufferedEvidenceWriter(),
                    new BufferedEvidenceWriter(projection is IBatchedEvidenceSink batched
                        ? writes => DeliverProjectionBatch(batched, writes) : null));
                buffers = [independent.CopyWriter, independent.ProjectionWriter];
            }
        }
        HostActivity.ConfigureW3C();
        listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Kora.Core" or "Kora.Application" or "Kora.Windows" or "Kora.Desktop",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = CaptureActivity,
        };
        ActivitySource.AddActivityListener(listener);
    }

    public ILogger CreateLogger(string categoryName) => new EvidenceLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;

    public void Dispose()
    {
#pragma warning disable VSTHRD002 // ILoggerProvider requires synchronous disposal; desktop service teardown runs on a worker.
        DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    public async ValueTask DisposeAsync()
    {
        await StopCapture().ConfigureAwait(false);
        await JoinBuffersAsync(buffers.Select(writer => writer.DisposeAsync().AsTask()),
            "Buffered evidence shutdown failed after all lanes were joined.").ConfigureAwait(false);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) =>
        JoinBuffersAsync(buffers.Select(writer => writer.FlushAsync(cancellationToken)),
            "Buffered evidence flush failed; independent lanes were attempted.");

    private static async Task JoinBuffersAsync(IEnumerable<Task> operations, string message)
    {
        var completion = Task.WhenAll(operations);
        await completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (completion.Exception is { } failures)
        {
            throw new IOException(message, failures.Flatten());
        }
        if (completion.IsCanceled) { throw new TaskCanceledException(completion); }
    }

    private void RequireBufferedHealth()
    {
        foreach (var writer in buffers) { writer.RequireHealthy(); }
    }

    private Task StopCapture()
    {
        lock (captureGate)
        {
            if (!disposed)
            {
                disposed = true;
                listener.Dispose();
            }
            if (captures == 0) { capturesStopped.TrySetResult(); }
#pragma warning disable VSTHRD003 // Completion is signalled by capture leases, never by dispatcher work.
            return capturesStopped.Task;
#pragma warning restore VSTHRD003
        }
    }

    private CaptureLease? BeginCapture(bool required)
    {
        lock (captureGate)
        {
            if (disposed)
            {
                ObjectDisposedException.ThrowIf(required, this);
                return null;
            }
            captures++;
            return new CaptureLease(this);
        }
    }

    private sealed class CaptureLease(EvidenceLoggerProvider owner) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.captureGate)
            {
                if (--owner.captures == 0 && owner.disposed) { owner.capturesStopped.TrySetResult(); }
            }
        }
    }

    private void CaptureActivity(Activity activity)
    {
        using var capture = BeginCapture(required: false);
        if (capture is null) { return; }
        var host = HostActivity.Current;
        if (host is null || !ReferenceEquals(host.Activity, activity))
        {
            return;
        }
        var trace = TraceSnapshot.Capture(activity)!;
        var completed = new CompletedActivityEnvelope(
            new(Guid.NewGuid()), trace, host.Request,
            new DateTimeOffset(activity.StartTimeUtc),
            new DateTimeOffset(activity.StartTimeUtc + activity.Duration),
            host.Outcome, activity.Links.Select(link =>
                new ActivityLinkEnvelope(link.Context.TraceId.ToHexString(), link.Context.SpanId.ToHexString())).ToArray(),
            host.CorrelationId, host.ApprovalId);
        if (buffers.Length == 0)
        {
            FanOut(completed.EvidenceId, sink => sink.WriteActivity(completed), ordinary: true);
        }
        else
        {
            QueueOrdinary(completed.EvidenceId, sink => sink.PrepareActivity(completed),
                sink => sink.PrepareActivityWrite(completed));
        }
    }

    private void Capture<TState>(string category, LogLevel level, EventId eventId,
        TState state, Exception? exception)
    {
        using var capture = BeginCapture(required: true);
        RequireBufferedHealth();
        var identity = new HostId<EvidenceIdentity>(Guid.NewGuid());
        DiagnosticEnvelope envelope;
        EvidenceGap? contextGap = null;
        try
        {
            var capturedScopes = new List<IReadOnlyDictionary<string, EvidenceValue>>();
            scopes.ForEachScope((scope, list) =>
            {
                if (list.Count >= 8)
                {
                    throw new InvalidDataException("Logging scope depth exceeds its bound.");
                }
                list.Add(EvidenceProperties.Capture(scope));
            }, capturedScopes);
            var host = HostActivity.Current;
            envelope = new DiagnosticEnvelope(1, identity, clock.GetUtcNow(),
                eventId.Id, eventId.Name, level.ToString(), category,
                EvidenceProperties.Template(state), CaptureProperties(state, host is null),
                capturedScopes.AsReadOnly(), TraceSnapshot.Capture(host?.Activity),
                host?.Request, exception?.GetType().FullName, host?.CorrelationId, host?.ApprovalId);
            if (state is TrustedAuditState)
            {
                HostActivity.RequireCurrent();
            }
            else if (host is null)
            {
                contextGap = new EvidenceGap(clock.GetUtcNow(), "capture",
                    EvidenceGapReason.MissingHostContext, identity, null);
            }
        }
        catch (InvalidDataException invalid)
        {
            List<Exception> errors = [invalid];
            ReportGap(new EvidenceGap(clock.GetUtcNow(), "capture",
                EvidenceGapReason.InvalidStructuredState, identity, invalid.GetType().FullName), errors);
            if (errors.Count > 1)
            {
                throw new InvalidDataException("Structured evidence capture and gap reporting failed.",
                    new AggregateException(errors));
            }
            throw;
        }

        if (state is not TrustedAuditState && buffers.Length != 0)
        {
            QueueOrdinary(identity, sink => sink.PrepareDiagnostic(envelope),
                sink => sink.PrepareDiagnosticWrite(envelope),
                contextGap);
            return;
        }
        FanOut(identity, sink =>
        {
            if (state is TrustedAuditState audit)
            {
                sink.WriteAudit(new AuditEnvelope(envelope, audit.Event));
            }
            else
            {
                sink.WriteDiagnostic(envelope);
            }
        }, ordinary: state is not TrustedAuditState,
            timeRendererAudit: state is TrustedAuditState timedAudit && PresenceSwitchTiming.IsRendererAudit(timedAudit.Event));
        if (contextGap is not null)
        {
            gaps.Report(contextGap);
        }
    }

    private void QueueOrdinary(HostId<EvidenceIdentity> identity, Func<IDeferredEvidenceSink, Action> prepare,
        Func<IBatchedEvidenceSink, IPreparedEvidenceWrite> prepareBatch,
        EvidenceGap? contextGap = null)
    {
        RequireBufferedHealth();
        var writes = new Dictionary<IEvidenceSink, Action>(ReferenceEqualityComparer.Instance);
        IPreparedEvidenceWrite? preparedProjection = null;
        FanOut(identity, sink =>
        {
            if (ReferenceEquals(sink, independent?.Projection) && sink is IBatchedEvidenceSink batched)
            {
                var prepared = prepareBatch(batched);
                if (prepared is null || prepared.EvidenceId != identity)
                {
                    throw new InvalidOperationException("The prepared projection must retain its admitted evidence identity.");
                }
                preparedProjection = prepared;
            }
            else { writes.Add(sink, prepare((IDeferredEvidenceSink)sink)); }
        }, ordinary: true, serialize: false);
        if (independent is { } delivery)
        {
            var projected = preparedProjection is not null
                ? delivery.ProjectionWriter.TryEnqueue(preparedProjection)
                : !writes.TryGetValue(delivery.Projection, out var project)
                    || delivery.ProjectionWriter.TryEnqueue(() =>
                        Deliver(identity, _ => project(), ordinary: true, serialize: true, [delivery.Projection]));
            var backpressure = projected ? null : new EvidenceGap(clock.GetUtcNow(), delivery.Projection.Name,
                EvidenceGapReason.ProjectionBackpressure, identity, null);
            delivery.CopyWriter.Enqueue(() =>
            {
                var errors = new List<Exception>();
                try
                {
                    Deliver(identity, _ => writes[delivery.Copy](), ordinary: true, serialize: true, [delivery.Copy]);
                }
                catch (IOException exception) { errors.Add(exception); }
                if (backpressure is not null) { ReportGap(backpressure, errors); }
                if (contextGap is not null) { ReportGap(contextGap, errors); }
                if (errors.Count != 0)
                {
                    throw new IOException("Required ordinary evidence copy or gap delivery failed.", new AggregateException(errors));
                }
            });
            return;
        }
        buffer!.Enqueue(() =>
        {
            FanOut(identity, sink =>
            {
                // A preparation rejected by an explicitly unavailable ordinary policy already reported its gap.
                if (writes.TryGetValue(sink, out var write)) { write(); }
            }, ordinary: true);
            if (contextGap is not null) { gaps.Report(contextGap); }
        });
    }

    private static ReadOnlyDictionary<string, EvidenceValue> CaptureProperties<TState>(TState state, bool missingHost)
    {
        var properties = new Dictionary<string, EvidenceValue>(EvidenceProperties.Capture(state), StringComparer.Ordinal);
        properties.Remove("kora.bootstrap");
        properties.Remove("kora.evidence.gap");
        if (missingHost)
        {
            properties.Add("kora.bootstrap", new(EvidenceValueKind.Boolean, "false"));
            properties.Add("kora.evidence.gap", new(EvidenceValueKind.Text, nameof(EvidenceGapReason.MissingHostContext)));
        }
        return new ReadOnlyDictionary<string, EvidenceValue>(properties);
    }

    private void FanOut(HostId<EvidenceIdentity> identity, Action<IEvidenceSink> write, bool ordinary,
        bool serialize = true, bool timeRendererAudit = false)
        => Deliver(identity, write, ordinary, serialize, sinks, timeRendererAudit);

    private void DeliverProjectionBatch(IBatchedEvidenceSink sink, IReadOnlyList<IPreparedEvidenceWrite> writes)
    {
        try
        {
            using var delivery = deliveryGates[sink].Enter(required: false);
            sink.WriteBatch(writes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            List<Exception> errors = [exception];
            foreach (var write in writes)
            {
                ReportGap(new EvidenceGap(clock.GetUtcNow(), sink.Name,
                    exception is Kora.Core.Storage.StorageAdmissionException
                        ? EvidenceGapReason.StorageNotAdmitted : EvidenceGapReason.SinkFailure,
                    write.EvidenceId, exception.GetType().FullName), errors);
            }
            if (exception is not DiagnosticRetentionUnavailableException || errors.Count != 1)
            {
                throw new IOException("Batched projection delivery or gap reporting failed.", new AggregateException(errors));
            }
        }
    }

    private void Deliver(HostId<EvidenceIdentity> identity, Action<IEvidenceSink> write, bool ordinary,
        bool serialize, ReadOnlySpan<IEvidenceSink> targets, bool timeRendererAudit = false)
    {
        var failures = new List<EvidenceGap>();
        var errors = new List<Exception>();
        var requiredFailure = false;
        foreach (var sink in targets)
        {
            try
            {
                if (serialize)
                {
                    var timeCopy = timeRendererAudit && ReferenceEquals(sink, independent?.Copy);
                    var timeProjection = timeRendererAudit && ReferenceEquals(sink, independent?.Projection);
                    if (timeCopy) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.AuditCopyGateWaiting); }
                    if (timeProjection) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.AuditProjectionGateWaiting); }
                    using var delivery = deliveryGates[sink].Enter(required: !ordinary);
                    if (timeCopy) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.AuditCopyGateEntered); }
                    if (timeProjection) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.AuditProjectionGateEntered); }
                    write(sink);
                    if (timeCopy) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.AuditCopyReturned); }
                    if (timeProjection) { PresenceSwitchTiming.Mark(PresenceSwitchPhase.AuditProjectionReturned); }
                }
                else { write(sink); }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // An explicitly unavailable ordinary policy reports a gap and still attempts the file
                // sink. It must not break unrelated required audit/session authority on activity disposal.
                requiredFailure |= !ordinary || ReferenceEquals(sink, independent?.Copy)
                    || exception is not DiagnosticRetentionUnavailableException;
                errors.Add(exception);
                failures.Add(new EvidenceGap(clock.GetUtcNow(), sink.Name,
                    exception is Kora.Core.Storage.StorageAdmissionException
                        ? EvidenceGapReason.StorageNotAdmitted : EvidenceGapReason.SinkFailure,
                    identity, exception.GetType().FullName));
            }
        }
        var sinkFailureCount = errors.Count;
        foreach (var gap in failures)
        {
            ReportGap(gap, errors);
        }
        if (requiredFailure || errors.Count > sinkFailureCount)
        {
            throw new IOException("Required evidence delivery or gap reporting failed; independent sinks were attempted.",
                new AggregateException(errors));
        }
    }

    private void ReportGap(EvidenceGap gap, List<Exception> errors)
    {
        try
        {
            if (gaps is IEvidenceSink sink && deliveryGates.TryGetValue(sink, out var gate))
            {
                using var delivery = gate.Enter(required: false);
                gaps.Report(gap);
            }
            else { gaps.Report(gap); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidOperationException or InvalidDataException)
        {
            // Reporting failures must not replace original causes or skip other pending reports.
            errors.Add(exception);
        }
    }

    private sealed class EvidenceLogger(EvidenceLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner.scopes.Push(state);
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                owner.Capture(category, logLevel, eventId, state, exception);
            }
        }
    }
}
