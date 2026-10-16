using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora.Core.Authorization;

/// <summary>Passive exact predicates on the committed event, not its separately retained requested review.</summary>
public sealed record ExactGrantHistoryQuery
{
    public ExactGrantHistoryQuery(SecurityAuditOutcome? outcome = null, string? reasonCode = null,
        SecurityAuditInitiator? initiator = null, EvidenceSegmentStatus? retention = null,
        DateTimeOffset? committedFromUtc = null, DateTimeOffset? committedUntilUtc = null,
        DateTimeOffset? dueFromUtc = null, DateTimeOffset? dueUntilUtc = null)
    {
        if (outcome is { } value && !Enum.IsDefined(value))
        {
            throw new InvalidDataException("Select a defined security audit outcome or Any.");
        }
        if (initiator is { } source && !Enum.IsDefined(source))
        {
            throw new InvalidDataException("Select a defined security audit initiator or Any.");
        }
        if (retention is not null and not EvidenceSegmentStatus.Present and not EvidenceSegmentStatus.ExpiredButPresent)
        {
            throw new InvalidDataException("Select Any, Present or ExpiredButPresent for complete retained history.");
        }
        ValidateRange(committedFromUtc, committedUntilUtc);
        ValidateRange(dueFromUtc, dueUntilUtc);
        Outcome = outcome;
        Initiator = initiator;
        Retention = retention;
        CommittedFromUtc = committedFromUtc;
        CommittedUntilUtc = committedUntilUtc;
        DueFromUtc = dueFromUtc;
        DueUntilUtc = dueUntilUtc;
        ReasonCode = reasonCode is null ? null : SecurityAuditEvent.ValidateReasonCode(reasonCode);
        var canonical = "exact-grant-history-query-v3\n"
            + (outcome is { } typed ? ((int)typed).ToString(CultureInfo.InvariantCulture) : "any")
            + "\n" + (reasonCode ?? string.Empty)
            + "\n" + (initiator is { } origin ? ((int)origin).ToString(CultureInfo.InvariantCulture) : "any")
            + "\n" + (retention is { } state ? ((int)state).ToString(CultureInfo.InvariantCulture) : "any")
            + "\n" + committedFromUtc?.ToString("O", CultureInfo.InvariantCulture)
            + "\n" + committedUntilUtc?.ToString("O", CultureInfo.InvariantCulture)
            + "\n" + dueFromUtc?.ToString("O", CultureInfo.InvariantCulture)
            + "\n" + dueUntilUtc?.ToString("O", CultureInfo.InvariantCulture);
        Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static ExactGrantHistoryQuery Unfiltered { get; } = new();
    public SecurityAuditOutcome? Outcome { get; }
    public SecurityAuditInitiator? Initiator { get; }
    public string? ReasonCode { get; }
    public EvidenceSegmentStatus? Retention { get; }
    public DateTimeOffset? CommittedFromUtc { get; }
    public DateTimeOffset? CommittedUntilUtc { get; }
    public DateTimeOffset? DueFromUtc { get; }
    public DateTimeOffset? DueUntilUtc { get; }
    public string Digest { get; }
    public static ImmutableArray<ExactGrantHistoryInitiatorOption> InitiatorOptions { get; } =
        [new(null), .. Enum.GetValues<SecurityAuditInitiator>().Select(value => new ExactGrantHistoryInitiatorOption(value))];
    public static ImmutableArray<string> RetentionOptions { get; } = ["Any", "Present", "ExpiredButPresent"];

    /// <summary>Only Any and empty text mean unset; no trimming, case folding or numeric enum parsing.</summary>
    public static ExactGrantHistoryQuery FromInput(string outcome, string reasonCode, string initiator = "Any",
        string retention = "Any", string committedFromUtc = "", string committedUntilUtc = "",
        string dueFromUtc = "", string dueUntilUtc = "")
    {
        SecurityAuditOutcome? parsed = outcome switch
        {
            "Any" => null,
            nameof(SecurityAuditOutcome.Requested) => SecurityAuditOutcome.Requested,
            nameof(SecurityAuditOutcome.Succeeded) => SecurityAuditOutcome.Succeeded,
            nameof(SecurityAuditOutcome.Failed) => SecurityAuditOutcome.Failed,
            nameof(SecurityAuditOutcome.Denied) => SecurityAuditOutcome.Denied,
            nameof(SecurityAuditOutcome.Cancelled) => SecurityAuditOutcome.Cancelled,
            nameof(SecurityAuditOutcome.Unknown) => SecurityAuditOutcome.Unknown,
            _ => throw new InvalidDataException("Select Any or a defined security audit outcome."),
        };
        var parsedInitiator = InitiatorOptions.FirstOrDefault(option => string.Equals(option.Label, initiator, StringComparison.Ordinal))
            ?? throw new InvalidDataException("Select Any or a defined security audit initiator.");
        ArgumentNullException.ThrowIfNull(reasonCode);
        EvidenceSegmentStatus? parsedRetention = retention switch
        {
            "Any" => null,
            "Present" => EvidenceSegmentStatus.Present,
            "ExpiredButPresent" => EvidenceSegmentStatus.ExpiredButPresent,
            _ => throw new InvalidDataException("Select Any, Present or ExpiredButPresent for complete retained history."),
        };
        return new(parsed, reasonCode.Length == 0 ? null : reasonCode, parsedInitiator.Value, parsedRetention,
            OptionalCanonicalUtc.Parse(committedFromUtc), OptionalCanonicalUtc.Parse(committedUntilUtc),
            OptionalCanonicalUtc.Parse(dueFromUtc), OptionalCanonicalUtc.Parse(dueUntilUtc));
    }

    private static void ValidateRange(DateTimeOffset? from, DateTimeOffset? until)
    {
        if (from is { Offset: var fromOffset } && fromOffset != TimeSpan.Zero
            || until is { Offset: var untilOffset } && untilOffset != TimeSpan.Zero
            || from > until)
        {
            throw new InvalidDataException("History time bounds must be UTC with From <= Until. Empty means Any.");
        }
    }

    public bool Matches(ExactGrantHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // Storage validates the complete envelope and provenance before exclusion; reject invalid projected metadata too.
        if (entry.Audit is null || entry.CommittedAt.Offset != TimeSpan.Zero || entry.DueAt.Offset != TimeSpan.Zero
            || entry.DueAt < entry.CommittedAt
            || entry.Retention is not EvidenceSegmentStatus.Present and not EvidenceSegmentStatus.ExpiredButPresent)
        {
            throw new InvalidDataException("Exact history requires complete validated committed metadata.");
        }
        var audit = entry.Audit;
        return (Outcome is not { } outcome || outcome == audit.Outcome)
            && (ReasonCode is null || string.Equals(ReasonCode, audit.ReasonCode, StringComparison.Ordinal))
            && (Initiator is not { } initiator || initiator == audit.Initiator)
            && (Retention is not { } retention || retention == entry.Retention)
            && (CommittedFromUtc is not { } committedFrom || entry.CommittedAt >= committedFrom)
            && (CommittedUntilUtc is not { } committedUntil || entry.CommittedAt <= committedUntil)
            && (DueFromUtc is not { } dueFrom || entry.DueAt >= dueFrom)
            && (DueUntilUtc is not { } dueUntil || entry.DueAt <= dueUntil);
    }
}
