using System.Text.Json;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>Exact finalized assistant data, never original input, an approval or evidence of an effect.</summary>
public sealed record ConversationResponse(ModelTurnProvenance Provenance, string Body)
{
    public int Version { get; init; } = 1;

    public void Validate()
    {
        if (Version != 1 || Body is null || Provenance is null || Provenance.Request is null
            || Provenance.Request.Origin != RequestOrigin.LocalUi || Provenance.Request.InvocationId is not null
            || Provenance.Selection is null || !Provenance.Selection.IsValid
            || Provenance.Selection.Provider != ModelProviderIdentity.Ollama)
        { throw new InvalidDataException("Assistant response provenance is invalid."); }
        Provenance.Turn.Validate();
        Provenance.Request.RequestId.Validate();
        Provenance.Request.SessionId.Validate();
        Provenance.Request.TaskId.Validate();
        ConversationText.Validate(Body, submission: true);
        if (JsonSerializer.SerializeToUtf8Bytes(new LocalModelResponse(Body, null)).Length
            > ModelContextEnvelope.MaximumOutputUtf8Bytes)
        { throw new InvalidDataException("The complete assistant answer exceeds the admitted output limit."); }
    }

    public static ConversationResponse FromResult(ModelTurnResult result, ModelTurnProvenance expected)
    {
        if (result.Outcome != ModelTurnOutcome.Succeeded || result.Provenance != expected
            || result.Response is not { Answer: not null, Action: null, GrantChange: null, Question: null } response)
        { throw new InvalidDataException("Only an exact admitted successful answer-only turn may retain assistant content."); }
        var answer = new ConversationResponse(expected, response.Answer);
        answer.Validate();
        return answer;
    }
}
