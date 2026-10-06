using System.Diagnostics;
using Kora.Application.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Diagnostics;

public sealed class EvidenceLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly IEvidenceSink[] sinks;
    private readonly IEvidenceGapReporter gaps;
    private readonly TimeProvider clock;
    private readonly ActivityListener listener;
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    public EvidenceLoggerProvider(IEnumerable<IEvidenceSink> sinks, IEvidenceGapReporter gaps,
        TimeProvider? clock = null)
    {
        this.sinks = sinks.ToArray();
        this.gaps = gaps;
        this.clock = clock ?? TimeProvider.System;
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

    public void Dispose() => listener.Dispose();

    private void CaptureActivity(Activity activity)
    {
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
        FanOut(completed.EvidenceId, sink => sink.WriteActivity(completed));
    }

    private void Capture<TState>(string category, LogLevel level, EventId eventId,
        TState state, Exception? exception)
    {
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
                EvidenceProperties.Template(state), EvidenceProperties.Capture(state),
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
            gaps.Report(new EvidenceGap(clock.GetUtcNow(), "capture",
                EvidenceGapReason.InvalidStructuredState, identity, invalid.GetType().FullName));
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
        });
        if (contextGap is not null)
        {
            gaps.Report(contextGap);
        }
    }

    private void FanOut(HostId<EvidenceIdentity> identity, Action<IEvidenceSink> write)
    {
        var failures = new List<EvidenceGap>();
        foreach (var sink in sinks)
        {
            try
            {
                write(sink);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failures.Add(new EvidenceGap(clock.GetUtcNow(), sink.Name,
                    exception is Kora.Core.Storage.StorageAdmissionException
                        ? EvidenceGapReason.StorageNotAdmitted : EvidenceGapReason.SinkFailure,
                    identity, exception.GetType().FullName));
            }
        }
        // Gap-reporting failure must not prevent either independent delivery attempt.
        foreach (var gap in failures)
        {
            gaps.Report(gap);
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
