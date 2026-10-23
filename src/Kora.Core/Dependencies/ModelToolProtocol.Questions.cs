using System.Collections.Immutable;
using System.Text.Json;

namespace Kora.Core.Dependencies;

public static partial class ModelToolProtocol
{
    public const int ClarificationSchemaVersion = 2;

    private static ModelClarificationQuestion ReadQuestion(JsonElement root)
    {
        RequireFields(root, "kind", "prompt", "options");
        var options = root.GetProperty("options");
        if (options.ValueKind != JsonValueKind.Array || options.GetArrayLength() > 4
            || options.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        { throw new InvalidDataException("Only declared bounded clarification labels are supported."); }
        try { return new(new(ReadString(root, "prompt"), options.EnumerateArray().Select(item => item.GetString()!).ToArray())); }
        catch (Kora.Core.Conversation.ConversationCredentialException exception)
        { throw new InvalidDataException("Credential-like clarification content is not admitted.", exception); }
    }

    private static void ValidateQuestionHistory(ImmutableArray<ModelToolMessage> history)
    {
        if (history.IsEmpty) { return; }
        if (history.Length != 2 || history[0] is not ModelClarificationQuestion question
            || history[1] is not ModelQuestionAnswerMessage answer || !ReferenceEquals(answer.Question, question))
        { throw new InvalidDataException("Only one complete exact clarification and host-validated answer may resume this turn."); }
    }
}
