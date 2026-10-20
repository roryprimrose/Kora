using Kora.Core.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Utilities;

namespace Kora.Application.Tools;

public sealed record UuidCommand(UuidOperation Operation, string? Canonical = null, bool ActivationPrefixed = false)
{
    public static IReadOnlyList<string> FixedPhrases { get; } = ["uuid generate", "uuid inspect", "uuid help"];

    public static UuidCommand? Parse(string input, string assistantName)
    {
        ArgumentNullException.ThrowIfNull(input);
        // Exact bare commands win over activation-name collisions.
        if (input.Length <= 128 && Exact(input) is { } bare) { return bare; }
        var name = AssistantNameRules.Normalize(assistantName);
        var reserved = ReservationHead(input).ToString();
        var text = reserved.Length == 0 ? reserved : ArtifactCommandRouter.RemoveActivationPrefix(reserved, name);
        if (input.Length <= 128 && input.Length == reserved.Length
            && !string.Equals(text, input, StringComparison.Ordinal) && Exact(text) is { } activated)
        {
            return activated with { ActivationPrefixed = true };
        }
        if (reserved.StartsWith(name, StringComparison.OrdinalIgnoreCase) && reserved.Length > name.Length
            && (char.IsWhiteSpace(reserved[name.Length]) || char.IsControl(reserved[name.Length])))
        { text = ReservationHead(reserved.AsSpan(name.Length)).ToString(); }
        return reserved.StartsWith("uuid", StringComparison.OrdinalIgnoreCase)
            || ReservationHead(text).StartsWith("uuid", StringComparison.OrdinalIgnoreCase)
            ? new(UuidOperation.Invalid) : null;
    }

    private static ReadOnlySpan<char> ReservationHead(ReadOnlySpan<char> text)
    {
        var start = 0;
        while (start < text.Length && (char.IsWhiteSpace(text[start]) || char.IsControl(text[start]))) { start++; }
        return text[start..];
    }

    private static UuidCommand? Exact(string text)
    {
        if (string.Equals(text, "uuid generate", StringComparison.Ordinal)) { return new(UuidOperation.Generate); }
        if (string.Equals(text, "uuid help", StringComparison.Ordinal)) { return new(UuidOperation.Help); }
        if (text.StartsWith("uuid inspect ", StringComparison.Ordinal))
        {
            try { return new(UuidOperation.Inspect, UtilityUuid.Parse(text[13..]).Canonical); }
            catch (InvalidDataException) { return null; }
        }
        return null;
    }
}
