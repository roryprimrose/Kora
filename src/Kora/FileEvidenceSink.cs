using System.Globalization;
using System.Text.Json;
using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Serilog.Events;
using Serilog.Parsing;

namespace Kora;

internal sealed class FileEvidenceSink(Serilog.Core.Logger logger, FileEvidenceHealth health) : IDeferredEvidenceSink, IEvidenceGapReporter
{
    private readonly MessageTemplateParser parser = new();

    public string Name => "daily-file";

    public void WriteDiagnostic(DiagnosticEnvelope envelope) => Write(envelope, audit: null);

    public void WriteAudit(AuditEnvelope envelope) => Write(envelope.Diagnostic, envelope.Audit);

    public Action PrepareDiagnostic(DiagnosticEnvelope envelope)
    {
        var snapshot = envelope with
        {
            Properties = new System.Collections.ObjectModel.ReadOnlyDictionary<string, EvidenceValue>(
                new Dictionary<string, EvidenceValue>(envelope.Properties, StringComparer.Ordinal)),
            Scopes = envelope.Scopes.Select(scope =>
                (IReadOnlyDictionary<string, EvidenceValue>)new System.Collections.ObjectModel.ReadOnlyDictionary<string, EvidenceValue>(
                    new Dictionary<string, EvidenceValue>(scope, StringComparer.Ordinal))).ToArray(),
        };
        return () => WriteDiagnostic(snapshot);
    }

    public Action PrepareActivity(CompletedActivityEnvelope envelope)
    {
        var snapshot = envelope with { Links = envelope.Links.ToArray() };
        return () => WriteActivity(snapshot);
    }

    public void WriteActivity(CompletedActivityEnvelope envelope)
    {
        health.RequireHealthy();
        logger.Information("Completed host activity {ActivityEvidence}.", JsonSerializer.Serialize(envelope));
        health.RequireHealthy();
    }

    public void Report(EvidenceGap gap)
    {
        health.RequireHealthy();
        logger.Warning("Evidence ingestion gap {EvidenceGap}.", JsonSerializer.Serialize(gap));
        health.RequireHealthy();
    }

    private void Write(DiagnosticEnvelope envelope, Kora.Core.Auditing.SecurityAuditEvent? audit)
    {
        health.RequireHealthy();
        var properties = envelope.Properties.Where(pair => pair.Key is not
            ("EvidenceEnvelope" or "EvidenceId" or "HostTraceId" or "HostSpanId"
             or "HostSessionId" or "HostTaskId" or "HostRequestId" or "TypedAuditEnvelope" or "EvidenceAuthority" or "SourceContext")).Select(pair =>
            new LogEventProperty(pair.Key, Scalar(pair.Value))).ToList();
        properties.Add(new LogEventProperty("SourceContext", new ScalarValue(envelope.Category)));
        properties.Add(new LogEventProperty("EvidenceAuthority", new ScalarValue(audit is null ? "Diagnostic" : "TypedAuditCopy")));
        properties.Add(new LogEventProperty("EvidenceEnvelope",
            new ScalarValue(Kora.Windows.Storage.WindowsSqliteEvidenceSink.EncodeDailyDiagnostic(envelope))));
        properties.Add(new LogEventProperty("EvidenceId", new ScalarValue(envelope.EvidenceId.Value)));
        if (envelope.Trace is { } trace)
        {
            properties.Add(new LogEventProperty("HostTraceId", new ScalarValue(trace.TraceId)));
            properties.Add(new LogEventProperty("HostSpanId", new ScalarValue(trace.SpanId)));
        }
        if (envelope.Host is { } host)
        {
            properties.Add(new LogEventProperty("HostSessionId", new ScalarValue(host.SessionId.Value)));
            properties.Add(new LogEventProperty("HostTaskId", new ScalarValue(host.TaskId.Value)));
            properties.Add(new LogEventProperty("HostRequestId", new ScalarValue(host.RequestId.Value)));
        }
        if (audit is not null)
        {
            properties.Add(new LogEventProperty("TypedAuditEnvelope", new ScalarValue(JsonSerializer.Serialize(audit))));
        }
        var level = envelope.Level switch
        {
            "Trace" => LogEventLevel.Verbose,
            "Debug" => LogEventLevel.Debug,
            "Information" => LogEventLevel.Information,
            "Warning" => LogEventLevel.Warning,
            "Error" => LogEventLevel.Error,
            "Critical" => LogEventLevel.Fatal,
            _ => throw new InvalidDataException("The diagnostic level is not supported."),
        };
        logger.ForContext("SourceContext", envelope.Category).Write(new LogEvent(envelope.ObservedUtc, level, exception: null,
            parser.Parse(envelope.MessageTemplate), properties));
        health.RequireHealthy();
    }

    private static ScalarValue Scalar(EvidenceValue value) => value.Kind switch
    {
        EvidenceValueKind.Null => new(null),
        EvidenceValueKind.Boolean => new(bool.Parse(value.CanonicalValue!)),
        EvidenceValueKind.WholeNumber => new(decimal.Parse(value.CanonicalValue!, CultureInfo.InvariantCulture)),
        EvidenceValueKind.Real => new(double.Parse(value.CanonicalValue!, CultureInfo.InvariantCulture)),
        EvidenceValueKind.Identifier => new(Guid.Parse(value.CanonicalValue!)),
        EvidenceValueKind.Timestamp => new(DateTimeOffset.Parse(value.CanonicalValue!, CultureInfo.InvariantCulture)),
        EvidenceValueKind.Text => new(value.CanonicalValue),
        _ => throw new InvalidDataException("The evidence value kind is not supported."),
    };
}
