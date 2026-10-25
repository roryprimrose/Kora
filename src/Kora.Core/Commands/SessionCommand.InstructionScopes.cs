namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public string? ScopeInstructionName { get; init; }
    public bool IsInstructionScope => Operation is SessionCommandOperation.InstructionScopeReview
        or SessionCommandOperation.InstructionScopeConfirm or SessionCommandOperation.InstructionScopeStop
        or SessionCommandOperation.InstructionScopeStatus;
    public const string InstructionScopeSyntax = "session instruction apply \"<enabled instruction name>\" | "
        + "session instruction confirm | session instruction stop | session instruction status. "
        + "First select the exact Active session in Sessions. Review displays the complete pinned source; "
        + "confirm separately. Only volatile ordinary local tool-free requests; no hosted, questions, skills or agents.";

    private static SessionCommand? ParseInstructionScopes(string text, string input)
    {
        const string prefix = "session instruction";
        if (!text.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)) { return null; }
        static SessionCommand Invalid() => new(SessionCommandOperation.Invalid, Error: InstructionScopeSyntax);
        if (System.Text.Encoding.UTF8.GetByteCount(input) > MaximumInputBytes || input.Any(char.IsControl))
        { return Invalid(); }
        var rest = text[prefix.Length..].Trim();
        var operation = rest.ToLowerInvariant() switch
        {
            "confirm" => SessionCommandOperation.InstructionScopeConfirm,
            "stop" => SessionCommandOperation.InstructionScopeStop,
            "status" or "help" => SessionCommandOperation.InstructionScopeStatus,
            _ => SessionCommandOperation.Invalid,
        };
        if (operation != SessionCommandOperation.Invalid) { return new(operation); }
        if (!rest.StartsWith("apply \"", StringComparison.OrdinalIgnoreCase) || !rest.EndsWith('"')) { return Invalid(); }
        var name = rest[7..^1];
        if (string.IsNullOrWhiteSpace(name) || name.Contains('"') || !string.Equals(name, name.Trim(), StringComparison.Ordinal)) { return Invalid(); }
        return new(SessionCommandOperation.InstructionScopeReview) { ScopeInstructionName = name };
    }
}
