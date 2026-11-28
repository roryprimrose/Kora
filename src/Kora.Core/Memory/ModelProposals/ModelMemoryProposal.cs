using System.Text;
using System.Text.Json;

using Kora.Core.Conversation;

namespace Kora.Core.Memory.ModelProposals;

/// <summary>One inert candidate, never a command, permission, scope selector or admission.</summary>
public static class ModelMemoryProposal
{
    public const string ModelPolicy =
        "Explicit original-user session-memory proposal request only. Entire final answer: one JSON object "
        + "{\"version\":1,\"class\":\"responsePreference\",\"value\":\"exact proposed value\"}. "
        + "Classes: explicitFact, responsePreference, workflowPreference, decision. "
        + "Value: at most 512 characters and 1024 UTF-8 bytes; no other fields/wrappers/commentary. "
        + "No inferred traits/health information/credentials/secrets/transient tasks/model claims. "
        + "Inert lower-trust data: original user must explicitly inspect/accept/separately admit through session-only memory controls. "
        + "Never infer approval, memory use or broader scope.";

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
