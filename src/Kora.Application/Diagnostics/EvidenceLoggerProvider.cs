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
    private readonly Lock captureGate = new();
    private readonly Lock deliveryGate = new();
    private readonly TaskCompletionSource capturesStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int captures;
    private bool disposed;
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    public EvidenceLoggerProvider(IEnumerable<IEvidenceSink> sinks, IEvidenceGapReporter gaps,
        TimeProvider? clock = null, bool bufferDiagnostics = false)
    {
        this.sinks = sinks.ToArray();
        this.gaps = gaps;
        this.clock = clock ?? TimeProvider.System;
        if (bufferDiagnostics)
        {
            if (this.sinks.Any(sink => sink is not IDeferredEvidenceSink))
            {
                throw new ArgumentException("Buffered sinks must support call-time evidence admission.", nameof(sinks));
            }
            buffer = new BufferedEvidenceWriter();
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
        if (buffer is not null) { await buffer.DisposeAsync().ConfigureAwait(false); }
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) =>
        buffer?.FlushAsync(cancellationToken) ?? Task.CompletedTask;

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
        if (buffer is null)
        {
            FanOut(completed.EvidenceId, sink => sink.WriteActivity(completed), ordinary: true);
        }
        else
        {
            QueueOrdinary(completed.EvidenceId, sink => sink.PrepareActivity(completed));
        }
    }

    private void Capture<TState>(string category, LogLevel level, EventId eventId,
        TState state, Exception? exception)
    {
        using var capture = BeginCapture(required: true);
        buffer?.RequireHealthy();
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

        if (state is not TrustedAuditState && buffer is not null)
        {
            QueueOrdinary(identity, sink => sink.PrepareDiagnostic(envelope),
                contextGap is null ? null : () => gaps.Report(contextGap));
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
        }, ordinary: state is not TrustedAuditState);
        if (contextGap is not null)
        {
            gaps.Report(contextGap);
        }
    }

    private void QueueOrdinary(HostId<EvidenceIdentity> identity, Func<IDeferredEvidenceSink, Action> prepare,
        Action? reportContextGap = null)
    {
        var writes = new Dictionary<IEvidenceSink, Action>();
        FanOut(identity, sink => writes.Add(sink, prepare((IDeferredEvidenceSink)sink)), ordinary: true, serialize: false);
        buffer!.Enqueue(() =>
        {
            FanOut(identity, sink =>
            {
                // A preparation rejected by an explicitly unavailable ordinary policy already reported its gap.
                if (writes.TryGetValue(sink, out var write)) { write(); }
            }, ordinary: true);
            reportContextGap?.Invoke();
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

    private void FanOut(HostId<EvidenceIdentity> identity, Action<IEvidenceSink> write, bool ordinary, bool serialize = true)
    {
        if (serialize)
        {
            lock (deliveryGate) { Deliver(identity, write, ordinary); }
        }
        else { Deliver(identity, write, ordinary); }
    }

    private void Deliver(HostId<EvidenceIdentity> identity, Action<IEvidenceSink> write, bool ordinary)
    {
        var failures = new List<EvidenceGap>();
        var errors = new List<Exception>();
        var requiredFailure = false;
        foreach (var sink in sinks)
        {
            try
            {
                write(sink);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // An explicitly unavailable ordinary policy reports a gap and still attempts the file
                // sink. It must not break unrelated required audit/session authority on activity disposal.
                requiredFailure |= !ordinary || exception is not DiagnosticRetentionUnavailableException;
                errors.Add(exception);
                failures.Add(SinkGap(sink, identity, exception));
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

    private EvidenceGap SinkGap(IEvidenceSink sink, HostId<EvidenceIdentity> identity, Exception exception) =>
        new(clock.GetUtcNow(), sink.Name,
            exception is Kora.Core.Storage.StorageAdmissionException
                ? EvidenceGapReason.StorageNotAdmitted : EvidenceGapReason.SinkFailure,
            identity, exception.GetType().FullName);

    private void ReportGap(EvidenceGap gap, List<Exception> errors)
    {
        try
        {
            gaps.Report(gap);
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
