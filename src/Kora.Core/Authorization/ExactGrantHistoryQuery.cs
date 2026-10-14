using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Auditing;

namespace Kora.Core.Authorization;

/// <summary>Passive exact predicates on the committed event, not its separately retained requested review.</summary>
public sealed record ExactGrantHistoryQuery
{
    public ExactGrantHistoryQuery(SecurityAuditOutcome? outcome = null, string? reasonCode = null)
    {
        if (outcome is { } value && !Enum.IsDefined(value))
        {
            throw new InvalidDataException("Select a defined security audit outcome or Any.");
        }
        Outcome = outcome;
        ReasonCode = reasonCode is null ? null : SecurityAuditEvent.ValidateReasonCode(reasonCode);
        var canonical = "exact-grant-history-query-v1\n"
            + (outcome is { } typed ? ((int)typed).ToString(CultureInfo.InvariantCulture) : "any")
            + "\n" + (reasonCode ?? string.Empty);
        Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static ExactGrantHistoryQuery Unfiltered { get; } = new();
    public SecurityAuditOutcome? Outcome { get; }
    public string? ReasonCode { get; }
    public string Digest { get; }

    /// <summary>Only Any and empty text mean unset; no trimming, case folding or numeric enum parsing.</summary>
    public static ExactGrantHistoryQuery FromInput(string outcome, string reasonCode)
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
        ArgumentNullException.ThrowIfNull(reasonCode);
        return new(parsed, reasonCode.Length == 0 ? null : reasonCode);
    }

    public bool Matches(SecurityAuditEvent audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        return (Outcome is not { } outcome || outcome == audit.Outcome)
            && (ReasonCode is null || string.Equals(ReasonCode, audit.ReasonCode, StringComparison.Ordinal));
    }
}
