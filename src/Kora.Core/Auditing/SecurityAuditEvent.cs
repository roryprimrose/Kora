namespace Kora.Core.Auditing;

public sealed class SecurityAuditEvent
{
    private const int MaximumIdentifierLength = 128;

    public SecurityAuditEvent(
        Guid correlationId,
        SecurityAuditCategory category,
        string actionId,
        SecurityAuditOutcome outcome,
        SecurityAuditInitiator initiator,
        string targetId,
        Guid? approvalId = null,
        string? reasonCode = null)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("The audit correlation identifier cannot be empty.", nameof(correlationId));
        }

        if (!Enum.IsDefined(category))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "The audit category is invalid.");
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The audit outcome is invalid.");
        }

        if (!Enum.IsDefined(initiator))
        {
            throw new ArgumentOutOfRangeException(nameof(initiator), initiator, "The audit initiator is invalid.");
        }

        CorrelationId = correlationId;
        Category = category;
        ActionId = ValidateIdentifier(actionId, nameof(actionId));
        Outcome = outcome;
        Initiator = initiator;
        TargetId = ValidateIdentifier(targetId, nameof(targetId));
        ApprovalId = approvalId;
        ReasonCode = reasonCode is null
            ? null
            : ValidateIdentifier(reasonCode, nameof(reasonCode));
    }

    public Guid CorrelationId { get; }

    public SecurityAuditCategory Category { get; }

    public string ActionId { get; }

    public SecurityAuditOutcome Outcome { get; }

    public SecurityAuditInitiator Initiator { get; }

    public string TargetId { get; }

    public Guid? ApprovalId { get; }

    public string? ReasonCode { get; }

    public SecurityAuditEvent WithOutcome(
        SecurityAuditOutcome outcome,
        string? reasonCode = null) =>
        new(
            CorrelationId,
            Category,
            ActionId,
            outcome,
            Initiator,
            TargetId,
            ApprovalId,
            reasonCode);

    private static string ValidateIdentifier(string identifier, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier, parameterName);
        if (identifier.Length > MaximumIdentifierLength
            || identifier.Any(character => !IsIdentifierCharacter(character)))
        {
            throw new ArgumentException(
                "Audit identifiers must contain at most 128 lowercase ASCII letters, digits, periods, or hyphens.",
                parameterName);
        }

        return identifier;
    }

    private static bool IsIdentifierCharacter(char character) =>
        character is >= 'a' and <= 'z'
        or >= '0' and <= '9'
        or '.'
        or '-';
}
