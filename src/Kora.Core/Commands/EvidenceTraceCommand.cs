using Kora.Core.Artifacts;
using Kora.Core.Diagnostics;

namespace Kora.Core.Commands;

/// <summary>Exact, inert diagnostic correlation preparation; never trace or session authority.</summary>
public sealed class EvidenceTraceCommand
{
    private EvidenceTraceCommand(string? traceId, bool help, bool activated)
    {
        TraceId = traceId;
        IsHelp = help;
        IsActivated = activated;
    }

    public const string Syntax = "prepare evidence trace <trace-id> | evidence trace help. "
        + "Use exactly 32 lowercase ASCII hexadecimal characters, not all zero; no GUID, header, link, trimming or repair. "
        + "Preparation only focuses the existing native Evidence viewer and sets its Trace correlation filter. "
        + "All other filters and the selected source remain AND-composed. Click Search / refresh separately to query the bounded retained reader. "
        + "Trace correlation is untrusted diagnostic data, never session selection, identity, ownership, permission, approval or authority. "
        + "No evidence read, scan, source selection, retention change, export, clipboard, model or network operation is performed.";

    public static IReadOnlyList<string> DiscoveryPhrases { get; } =
        Array.AsReadOnly(new[] { "prepare evidence trace", "evidence trace help" });
    public string? TraceId { get; }
    public bool IsHelp { get; }
    public bool IsActivated { get; }
    public bool IsValid => IsHelp || TraceId is not null;
    public override string ToString() => nameof(EvidenceTraceCommand);

    public static EvidenceTraceCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        // Exact unprefixed syntax wins over a colliding assistant name.
        const string prefix = "prepare evidence trace ";
        var text = input;
        var activated = false;
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "evidence trace help", StringComparison.OrdinalIgnoreCase))
        {
            text = ArtifactCommandRouter.RemoveActivationPrefix(text, assistantName);
            activated = !string.Equals(text, input, StringComparison.Ordinal);
        }
        if (!Reserved(text)) { return Reserved(input) ? new(null, false, false) : null; }
        if (input.Any(char.IsControl)) { return new(null, false, activated); }
        if (string.Equals(text, "evidence trace help", StringComparison.OrdinalIgnoreCase))
        {
            return new(null, true, activated);
        }
        var trace = text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? text[prefix.Length..] : null;
        return new(TraceSnapshot.IsValidW3CId(trace, 32) ? trace : null, false, activated);
    }

    private static bool Reserved(string text)
    {
        var normalized = BuiltInCommandRouter.Normalize(text);
        return string.Equals(normalized, "evidence", StringComparison.Ordinal)
            || normalized.StartsWith("evidence trace", StringComparison.Ordinal)
            || normalized.StartsWith("prepare evidence", StringComparison.Ordinal);
    }
}
