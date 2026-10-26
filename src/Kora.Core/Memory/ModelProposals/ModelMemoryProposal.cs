using System.Text;
using System.Text.Json;

using Kora.Core.Conversation;

namespace Kora.Core.Memory.ModelProposals;

/// <summary>One inert candidate, never a command, permission, scope selector or admission.</summary>
public static class ModelMemoryProposal
{
    public const string ModelPolicy =
        "Only when the original user explicitly requests a session-memory proposal, the entire final answer may be "
        + "one JSON object: {\"version\":1,\"class\":\"responsePreference\",\"value\":\"exact proposed value\"}. "
        + "Allowed class names: explicitFact, responsePreference, workflowPreference, decision. "
        + "At most 512 value characters and 1024 UTF-8 value bytes. No other fields, wrappers or commentary. "
        + "Do not propose inferred traits, health information, credentials, secrets, transient tasks or model claims. "
        + "This is inert lower-trust data. Only the original user can explicitly inspect, accept and separately admit "
        + "it through Kora's session-only memory controls. Never infer approval, memory use or broader scope.";

    public static MemoryCandidate Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(text) > MemoryPolicy.MaximumCandidateUtf8Bytes)
            { throw new InvalidDataException("The complete memory proposal exceeds the bounded payload limit."); }
            using var document = JsonDocument.Parse(text, new() { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            { throw new InvalidDataException("A memory proposal must be one exact object."); }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name is not ("version" or "class" or "value") || !names.Add(property.Name))
                { throw new InvalidDataException("Unknown or duplicate memory proposal field."); }
            }
            if (names.Count != 3 || !root.GetProperty("version").TryGetInt32(out var version) || version != 1
                || root.GetProperty("class").ValueKind != JsonValueKind.String
                || root.GetProperty("value").ValueKind != JsonValueKind.String)
            { throw new InvalidDataException("Memory proposal version, classification or value is invalid."); }
            var classification = root.GetProperty("class").GetString() switch
            {
                "explicitFact" => MemoryContentClass.ExplicitFact,
                "responsePreference" => MemoryContentClass.ResponsePreference,
                "workflowPreference" => MemoryContentClass.WorkflowPreference,
                "decision" => MemoryContentClass.Decision,
                _ => MemoryContentClass.Unknown,
            };
            var candidate = new MemoryCandidate(classification, root.GetProperty("value").GetString()!);
            var reason = MemoryPolicy.ValidateCandidate(candidate);
            if (reason != MemoryReason.None)
            { throw new InvalidDataException("Memory proposal rejected: " + reason + "."); }
            ConversationText.Validate(candidate.Value!, submission: true);
            return candidate;
        }
        catch (Exception exception) when (exception is JsonException or EncoderFallbackException or InvalidOperationException)
        { throw new InvalidDataException("The strict memory proposal format is invalid.", exception); }
    }
}
