using System.Text;
using System.Text.Json;

namespace Kora.Core.Conversation;

/// <summary>Syntax classification only. An advisory conveys no source selection or authority.</summary>
public static class ModelHistoryContextProposal
{
    public const string ModelPolicy =
        "If answering the unchanged original question requires earlier conversation context, the entire final answer may be "
        + "exactly {\"version\":1,\"advisory\":\"HISTORYREVIEW\"}. No other fields, wrappers or commentary. "
        + "This inert advisory requests no history read or disclosure. Only the original user may select earlier whole "
        + "messages, inspect the complete sources and unchanged original question, and separately confirm one local-only "
        + "tool-free answer. Never supply message IDs, a replacement question, permissions or approval.";

    public static bool IsAdvisory(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(text) > 128) { return false; }
            using var document = JsonDocument.Parse(text, new() { MaxDepth = 1 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return false; }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name is not ("version" or "advisory") || !names.Add(property.Name)) { return false; }
            }
            return names.Count == 2
                && root.GetProperty("version").ValueKind == JsonValueKind.Number
                && root.GetProperty("version").TryGetInt32(out var version) && version == 1
                && root.GetProperty("advisory").ValueKind == JsonValueKind.String
                && string.Equals(root.GetProperty("advisory").GetString(), "HISTORYREVIEW", StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or EncoderFallbackException)
        { return false; }
    }
}
