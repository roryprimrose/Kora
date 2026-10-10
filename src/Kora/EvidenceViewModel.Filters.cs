using System.Globalization;
using System.Runtime.CompilerServices;

using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class EvidenceViewModel
{
    private EvidenceSource source;
    private string safeText = string.Empty;
    private string sessionFilter = string.Empty;
    private string taskFilter = string.Empty;
    private string traceFilter = string.Empty;
    private string requestFilter = string.Empty;
    private string invocationFilter = string.Empty;
    private string approvalFilter = string.Empty;
    private string correlationFilter = string.Empty;
    private string fromFilter = string.Empty;
    private string untilFilter = string.Empty;
    private EvidenceSeverity? severity;
    private SecurityAuditOutcome? auditOutcome;

    public static IReadOnlyList<EvidenceSeverity?> Severities { get; } = [null, .. Enum.GetValues<EvidenceSeverity>().Select(value => (EvidenceSeverity?)value)];
    public static IReadOnlyList<SecurityAuditOutcome?> AuditOutcomes { get; } = [null, .. Enum.GetValues<SecurityAuditOutcome>().Select(value => (SecurityAuditOutcome?)value)];

    public EvidenceSource Source { get => source; set => ChangeFilter(ref source, value); }
    public string SafeText { get => safeText; set => ChangeFilter(ref safeText, value); }
    public string SessionFilter { get => sessionFilter; set => ChangeFilter(ref sessionFilter, value); }
    public string TaskFilter { get => taskFilter; set => ChangeFilter(ref taskFilter, value); }
    public string TraceFilter { get => traceFilter; set => ChangeFilter(ref traceFilter, value); }
    public string RequestFilter { get => requestFilter; set => ChangeFilter(ref requestFilter, value); }
    public string InvocationFilter { get => invocationFilter; set => ChangeFilter(ref invocationFilter, value); }
    public string ApprovalFilter { get => approvalFilter; set => ChangeFilter(ref approvalFilter, value); }
    public string CorrelationFilter { get => correlationFilter; set => ChangeFilter(ref correlationFilter, value); }
    public string FromFilter { get => fromFilter; set => ChangeFilter(ref fromFilter, value); }
    public string UntilFilter { get => untilFilter; set => ChangeFilter(ref untilFilter, value); }
    public EvidenceSeverity? Severity { get => severity; set => ChangeFilter(ref severity, value); }
    public SecurityAuditOutcome? AuditOutcome { get => auditOutcome; set => ChangeFilter(ref auditOutcome, value); }

    public void ClearAdvancedFilters()
    {
        RequestFilter = string.Empty;
        InvocationFilter = string.Empty;
        ApprovalFilter = string.Empty;
        CorrelationFilter = string.Empty;
        FromFilter = string.Empty;
        UntilFilter = string.Empty;
        Severity = null;
        AuditOutcome = null;
    }

    private void ChangeFilter<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) { return; }
        field = value;
        if (!closed)
        {
            filterRevision++;
            readCancellation?.Cancel();
            Clear("Filters changed. Search / refresh starts a fresh bounded snapshot; previous results and selection were cleared.");
        }
        OnPropertyChanged(name);
        Notify();
    }

    private EvidenceQuery CreateQuery() => new()
    {
        Source = Source, Text = string.IsNullOrEmpty(SafeText) ? null : SafeText,
        SessionId = Identity<SessionIdentity>(SessionFilter),
        TaskId = Identity<TaskIdentity>(TaskFilter),
        TraceId = string.IsNullOrEmpty(TraceFilter) ? null : TraceFilter,
        RequestId = Identity<RequestIdentity>(RequestFilter),
        InvocationId = Identity<InvocationIdentity>(InvocationFilter),
        ApprovalId = Identifier(ApprovalFilter),
        CorrelationId = Identifier(CorrelationFilter),
        FromUtc = Timestamp(FromFilter),
        UntilUtc = Timestamp(UntilFilter),
        Severity = Severity,
        AuditOutcome = AuditOutcome,
    };

    private static HostId<T>? Identity<T>(string value) =>
        Identifier(value) is { } id ? new HostId<T>(id) : null;

    private static Guid? Identifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        var id = Guid.ParseExact(value, "D");
        if (!string.Equals(value, id.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Evidence identifiers require hyphenated GUIDs without surrounding whitespace.");
        }
        return id;
    }

    private static DateTimeOffset? Timestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        var fraction = value.IndexOf('.');
        if (fraction >= 0 && (fraction + 1 == value.Length || !char.IsAsciiDigit(value[fraction + 1])))
        {
            throw new FormatException("An ISO fractional timestamp requires digits after its decimal point.");
        }
        string[] formats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
            "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];
        return DateTimeOffset.ParseExact(value, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
