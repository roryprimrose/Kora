using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Auditing;

namespace Kora.Core.Authorization;

/// <summary>Passive exact predicates on the committed event, not its separately retained requested review.</summary>
public sealed record ExactGrantHistoryQuery
{
    public ExactGrantHistoryQuery(SecurityAuditOutcome? outcome = null, string? reasonCode = null,
        SecurityAuditInitiator? initiator = null)
    {
        if (outcome is { } value && !Enum.IsDefined(value))
        {
            throw new InvalidDataException("Select a defined security audit outcome or Any.");
        }
        if (initiator is { } source && !Enum.IsDefined(source))
        {
            throw new InvalidDataException("Select a defined security audit initiator or Any.");
        }
        Outcome = outcome;
        Initiator = initiator;
        ReasonCode = reasonCode is null ? null : SecurityAuditEvent.ValidateReasonCode(reasonCode);
        var canonical = "exact-grant-history-query-v2\n"
            + (outcome is { } typed ? ((int)typed).ToString(CultureInfo.InvariantCulture) : "any")
            + "\n" + (reasonCode ?? string.Empty)
            + "\n" + (initiator is { } origin ? ((int)origin).ToString(CultureInfo.InvariantCulture) : "any");
        Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static ExactGrantHistoryQuery Unfiltered { get; } = new();
    public SecurityAuditOutcome? Outcome { get; }
    public SecurityAuditInitiator? Initiator { get; }
    public string? ReasonCode { get; }
    public string Digest { get; }
    public static ImmutableArray<ExactGrantHistoryInitiatorOption> InitiatorOptions { get; } =
        [new(null), .. Enum.GetValues<SecurityAuditInitiator>().Select(value => new ExactGrantHistoryInitiatorOption(value))];

    /// <summary>Only Any and empty text mean unset; no trimming, case folding or numeric enum parsing.</summary>
    public static ExactGrantHistoryQuery FromInput(string outcome, string reasonCode, string initiator = "Any")
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
        return new(parsed, reasonCode.Length == 0 ? null : reasonCode, parsedInitiator.Value);
    }

    public bool Matches(SecurityAuditEvent audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        return (Outcome is not { } outcome || outcome == audit.Outcome)
            && (ReasonCode is null || string.Equals(ReasonCode, audit.ReasonCode, StringComparison.Ordinal))
            && (Initiator is not { } initiator || initiator == audit.Initiator);
    }
}
