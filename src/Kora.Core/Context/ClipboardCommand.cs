using Kora.Core.Hosting;

namespace Kora.Core.Context;

public sealed record ClipboardCommand(ClipboardOperation Operation, Guid SnapshotId = default)
{
    private static readonly IReadOnlyDictionary<string, ClipboardOperation> Commands =
        new Dictionary<string, ClipboardOperation>(StringComparer.Ordinal)
        {
            ["preview clipboard"] = ClipboardOperation.Capture,
            ["preview the clipboard"] = ClipboardOperation.Capture,
            ["snapshot clipboard"] = ClipboardOperation.Capture,
            ["explain clipboard"] = ClipboardOperation.ExplainUnavailable,
            ["explain the clipboard"] = ClipboardOperation.ExplainUnavailable,
            ["clear clipboard preview"] = ClipboardOperation.Revoke,
            ["revoke clipboard snapshot"] = ClipboardOperation.Revoke,
            ["reuse clipboard snapshot"] = ClipboardOperation.Invalid,
        };

    public static IEnumerable<string> FixedPhrases => Commands.Keys;

    // Input has already passed the shared activation-name and exact phrase normalizer.
    public static ClipboardCommand? Parse(string normalized) =>
        Commands.TryGetValue(normalized, out var operation) ? new(operation) : ParseReuse(normalized);

    private static ClipboardCommand? ParseReuse(string normalized)
    {
        const string prefix = "reuse clipboard snapshot ";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) { return null; }
        // The shared normalizer replaces GUID punctuation with spaces.
        var id = normalized[prefix.Length..].Replace(" ", string.Empty, StringComparison.Ordinal);
        return Guid.TryParseExact(id, "N", out var snapshotId) && snapshotId != Guid.Empty
            ? new(ClipboardOperation.Reuse, snapshotId) : new(ClipboardOperation.Invalid);
    }

    public static bool IsDeliberateOrigin(RequestOrigin origin) =>
        origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice;
}
