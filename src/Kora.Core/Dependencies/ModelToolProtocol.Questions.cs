using System.Collections.Immutable;
using System.Text.Json;

namespace Kora.Core.Dependencies;

public static partial class ModelToolProtocol
{
    public const string AskUserToolName = "ask_user";
    public const int ClarificationSchemaVersion = 2;
    public const int ReadOnlyClarificationSchemaVersion = 3;

    public static bool AllowsClarification(ModelToolMode mode, ModelProviderIdentity provider) =>
        provider == ModelProviderIdentity.Ollama && mode is ModelToolMode.None or ModelToolMode.ReadOnly;

    private static ModelClarificationQuestion ReadQuestion(JsonElement root)
    {
        var bounded = root.TryGetProperty("minimum", out _);
        var tool = root.TryGetProperty("callId", out _);
        if (tool) { RequireFields(root, "kind", "prompt", "options", "minimum", "maximum", "callId"); }
        else if (bounded) { RequireFields(root, "kind", "prompt", "options", "minimum", "maximum"); }
        else { RequireFields(root, "kind", "prompt", "options"); }
        return ReadQuestionArguments(root, bounded, tool ? ReadString(root, "callId") : null);
    }

    public static ModelClarificationQuestion ParseAskUser(ReadOnlySpan<byte> arguments, string callId)
    {
        using var document = ReadJson(arguments, MaximumOutputUtf8Bytes);
        RequireFields(document.RootElement, "prompt", "options", "minimum", "maximum");
        return ReadQuestionArguments(document.RootElement, bounded: true, callId);
    }

    private static ModelClarificationQuestion ReadQuestionArguments(JsonElement root, bool bounded, string? callId)
    {
        var options = root.GetProperty("options");
        if (options.ValueKind != JsonValueKind.Array || options.GetArrayLength() > 4
            || options.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        { throw new InvalidDataException("Only declared bounded clarification labels are supported."); }
        var minimum = 1;
        var maximum = 1;
        if (bounded && (root.GetProperty("minimum").ValueKind != JsonValueKind.Number
            || root.GetProperty("maximum").ValueKind != JsonValueKind.Number
            || !root.GetProperty("minimum").TryGetInt32(out minimum)
            || !root.GetProperty("maximum").TryGetInt32(out maximum)))
        { throw new InvalidDataException("Question selection bounds must be integers."); }
        try { return new(new(ReadString(root, "prompt"), options.EnumerateArray().Select(item => item.GetString()!).ToArray(),
            minimum, maximum), callId); }
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

    private static void ValidateReadOnlyQuestion(ImmutableArray<ModelToolMessage> history, ref int index)
    {
        var question = (ModelClarificationQuestion)history[index++];
        if (question.CallId is null && question.Question.Options.Count == 0 || index >= history.Length
            || history[index++] is not ModelQuestionAnswerMessage answer || !ReferenceEquals(answer.Question, question))
        { throw new InvalidDataException("Read-only continuation requires one complete exact admitted clarification and answer."); }
    }
}
