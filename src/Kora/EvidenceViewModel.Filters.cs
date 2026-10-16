using System.Diagnostics;
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
    private string spanFilter = string.Empty;
    private string parentSpanFilter = string.Empty;
    private string traceSourceFilter = string.Empty;
    private string traceSourceVersionFilter = string.Empty;
    private TraceSourceVersionAvailability traceVersionAvailability;
    private string activityNameFilter = string.Empty;
    private ActivityKindChoice activityKind = ActivityKindChoices[0];
    private TraceFlagsChoice traceFlags = TraceFlagsChoices[0];
    private OutcomeChoice outcome = OutcomeChoices[0];
    private EvidenceContentAvailability contentAvailability;
    private string requestFilter = string.Empty;
    private string invocationFilter = string.Empty;
    private string approvalFilter = string.Empty;
    private string correlationFilter = string.Empty;
    private string fromFilter = string.Empty;
    private string untilFilter = string.Empty;
    private string eventIdFilter = string.Empty;
    private string categoryFilter = string.Empty;
    private string actionIdFilter = string.Empty;
    private string eventNameFilter = string.Empty;
    private string exceptionTypeFilter = string.Empty;
    private string fromAuditSequenceFilter = string.Empty;
    private string untilAuditSequenceFilter = string.Empty;
    private EvidenceSeverity? severity;
    private SecurityAuditOutcome? auditOutcome;

    public static IReadOnlyList<EvidenceSeverity?> Severities { get; } = [null, .. Enum.GetValues<EvidenceSeverity>().Select(value => (EvidenceSeverity?)value)];
    public static IReadOnlyList<SecurityAuditOutcome?> AuditOutcomes { get; } = [null, .. Enum.GetValues<SecurityAuditOutcome>().Select(value => (SecurityAuditOutcome?)value)];
    public sealed record OutcomeChoice(HostOperationOutcome? Value, string Label)
    {
        public override string ToString() => Label;
    }
    public static IReadOnlyList<OutcomeChoice> OutcomeChoices { get; } =
        [new(null, "Any"), .. Enum.GetValues<HostOperationOutcome>().Select(value => new OutcomeChoice(value, value.ToString()))];
    public static IReadOnlyList<EvidenceContentAvailability> ContentAvailabilities { get; } = Enum.GetValues<EvidenceContentAvailability>();
    public sealed record ActivityKindChoice(ActivityKind? Value, string Label)
    {
        public override string ToString() => Label;
    }
    public sealed record TraceFlagsChoice(ActivityTraceFlags? Value, string Label)
    {
        public override string ToString() => Label;
    }
    public static IReadOnlyList<ActivityKindChoice> ActivityKindChoices { get; } =
        [new(null, "Any"), .. Enum.GetValues<ActivityKind>().Select(value => new ActivityKindChoice(value, value.ToString()))];
    public static IReadOnlyList<TraceFlagsChoice> TraceFlagsChoices { get; } =
        [new(null, "Any"), .. Enum.GetValues<ActivityTraceFlags>().Select(value => new TraceFlagsChoice(value, value.ToString()))];
    public static IReadOnlyList<TraceSourceVersionAvailability> TraceVersionAvailabilities { get; } = Enum.GetValues<TraceSourceVersionAvailability>();

    public EvidenceSource Source { get => source; set => ChangeFilter(ref source, value); }
    public string SafeText { get => safeText; set => ChangeFilter(ref safeText, value); }
    public string SessionFilter { get => sessionFilter; set => ChangeFilter(ref sessionFilter, value); }
    public string TaskFilter { get => taskFilter; set => ChangeFilter(ref taskFilter, value); }
    public string TraceFilter { get => traceFilter; set => ChangeFilter(ref traceFilter, value); }
    public string SpanFilter { get => spanFilter; set => ChangeFilter(ref spanFilter, value); }
    public string ParentSpanFilter { get => parentSpanFilter; set => ChangeFilter(ref parentSpanFilter, value); }
    public string TraceSourceFilter { get => traceSourceFilter; set => ChangeFilter(ref traceSourceFilter, value); }
    public string TraceSourceVersionFilter { get => traceSourceVersionFilter; set => ChangeFilter(ref traceSourceVersionFilter, value); }
    public TraceSourceVersionAvailability TraceVersionAvailability { get => traceVersionAvailability; set => ChangeFilter(ref traceVersionAvailability, value); }
    public string ActivityNameFilter { get => activityNameFilter; set => ChangeFilter(ref activityNameFilter, value); }
    public ActivityKindChoice ActivityKind { get => activityKind; set => ChangeFilter(ref activityKind, value); }
    public TraceFlagsChoice TraceFlags { get => traceFlags; set => ChangeFilter(ref traceFlags, value); }
    public OutcomeChoice Outcome { get => outcome; set => ChangeFilter(ref outcome, value); }
    public EvidenceContentAvailability ContentAvailability { get => contentAvailability; set => ChangeFilter(ref contentAvailability, value); }
    public string RequestFilter { get => requestFilter; set => ChangeFilter(ref requestFilter, value); }
    public string InvocationFilter { get => invocationFilter; set => ChangeFilter(ref invocationFilter, value); }
    public string ApprovalFilter { get => approvalFilter; set => ChangeFilter(ref approvalFilter, value); }
    public string CorrelationFilter { get => correlationFilter; set => ChangeFilter(ref correlationFilter, value); }
    public string FromFilter { get => fromFilter; set => ChangeFilter(ref fromFilter, value); }
    public string UntilFilter { get => untilFilter; set => ChangeFilter(ref untilFilter, value); }
    public string EventIdFilter { get => eventIdFilter; set => ChangeFilter(ref eventIdFilter, value); }
    public string CategoryFilter { get => categoryFilter; set => ChangeFilter(ref categoryFilter, value); }
    public string ActionIdFilter { get => actionIdFilter; set => ChangeFilter(ref actionIdFilter, value); }
    public string EventNameFilter { get => eventNameFilter; set => ChangeFilter(ref eventNameFilter, value); }
    public string ExceptionTypeFilter { get => exceptionTypeFilter; set => ChangeFilter(ref exceptionTypeFilter, value); }
    public string FromAuditSequenceFilter { get => fromAuditSequenceFilter; set => ChangeFilter(ref fromAuditSequenceFilter, value); }
    public string UntilAuditSequenceFilter { get => untilAuditSequenceFilter; set => ChangeFilter(ref untilAuditSequenceFilter, value); }
    public EvidenceSeverity? Severity { get => severity; set => ChangeFilter(ref severity, value); }
    public SecurityAuditOutcome? AuditOutcome { get => auditOutcome; set => ChangeFilter(ref auditOutcome, value); }

    public void ClearAdvancedFilters()
    {
        ParentSpanFilter = string.Empty;
        TraceSourceFilter = string.Empty;
        TraceSourceVersionFilter = string.Empty;
        TraceVersionAvailability = TraceSourceVersionAvailability.Any;
        ActivityNameFilter = string.Empty;
        ActivityKind = ActivityKindChoices[0];
        TraceFlags = TraceFlagsChoices[0];
        Outcome = OutcomeChoices[0];
        ContentAvailability = EvidenceContentAvailability.Any;
        RequestFilter = string.Empty;
        InvocationFilter = string.Empty;
        ApprovalFilter = string.Empty;
        CorrelationFilter = string.Empty;
        FromFilter = string.Empty;
        UntilFilter = string.Empty;
        EventIdFilter = string.Empty;
        CategoryFilter = string.Empty;
        ActionIdFilter = string.Empty;
        EventNameFilter = string.Empty;
        ExceptionTypeFilter = string.Empty;
        FromAuditSequenceFilter = string.Empty;
        UntilAuditSequenceFilter = string.Empty;
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
        SpanId = string.IsNullOrEmpty(SpanFilter) ? null : SpanFilter,
        ParentSpanId = string.IsNullOrEmpty(ParentSpanFilter) ? null : ParentSpanFilter,
        TraceSource = ExactText(TraceSourceFilter),
        TraceSourceVersion = ExactText(TraceSourceVersionFilter),
        TraceSourceVersionAvailability = TraceVersionAvailability,
        ActivityName = ExactText(ActivityNameFilter),
        ActivityKind = ActivityKind.Value,
        TraceFlags = TraceFlags.Value,
        Outcome = Outcome.Value,
        ContentAvailability = ContentAvailability,
        RequestId = Identity<RequestIdentity>(RequestFilter),
        InvocationId = Identity<InvocationIdentity>(InvocationFilter),
        ApprovalId = Identifier(ApprovalFilter),
        CorrelationId = Identifier(CorrelationFilter),
        FromUtc = Timestamp(FromFilter),
        UntilUtc = Timestamp(UntilFilter),
        Severity = Severity,
        AuditOutcome = AuditOutcome,
        EventId = EventIdentifier(EventIdFilter),
        Category = ExactText(CategoryFilter),
        ActionId = ExactText(ActionIdFilter),
        EventName = ExactText(EventNameFilter),
        ExceptionType = ExactText(ExceptionTypeFilter),
        FromAuditSequence = AuditSequence(FromAuditSequenceFilter),
        UntilAuditSequence = AuditSequence(UntilAuditSequenceFilter),
    };

    private static long? AuditSequence(string value)
    {
        if (string.IsNullOrEmpty(value)) { return null; }
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
            || sequence <= 0 || !string.Equals(value, sequence.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new FormatException("Audit sequence requires a canonical invariant positive 64-bit integer.");
        }
        return sequence;
    }

    private static int? EventIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value)) { return null; }
        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id)
            || !string.Equals(value, id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            throw new FormatException("Event ID requires a canonical invariant signed 32-bit integer.");
        }
        return id;
    }

    private static string? ExactText(string value)
    {
        if (string.IsNullOrEmpty(value)) { return null; }
        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]))
        {
            throw new FormatException("Exact evidence text filters cannot contain surrounding whitespace.");
        }
        return value;
    }

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
