using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Dependencies;

public sealed partial class ModelToolProtocolTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void ReadOnlyQuestionCanOccurOnlyBetweenCompleteSerialExchangesWithoutChangingOldSchemas(int position)
    {
        var provenance = Provenance();
        var context = ModelToolProtocol.ReadContext(Envelope(provenance).Serialize(), provenance);
        var first = Calls(Tool("one"));
        var second = Calls(Tool("two"));
        var question = new ModelClarificationQuestion(new("Which observation?", ["Version", "Readiness", "Runtime"]));
        var answer = new ModelQuestionAnswerMessage(question, new(["option-0"]), RequestOrigin.ActivatedVoice);
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "recorded"));
        var history = new List<ModelToolMessage>
        {
            first, ModelToolProtocol.CreateToolResult(first.Calls[0], reply),
            second, ModelToolProtocol.CreateToolResult(second.Calls[0], reply),
        };
        history.InsertRange(position, [question, answer]);
        var request = ModelToolProtocol.CreateRequest(provenance, null, context, history, 4, clarificationAllowed: true);
        request.Messages.Should().Equal(history);
        request.Context.Tools.Select(tool => tool.Id).Should().Equal(ReadOnlyCapabilityCatalog.Descriptors.Select(tool => tool.Id));
        using var wire = JsonDocument.Parse(ModelToolProtocol.SerializeRequest(request));
        wire.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(3);
        wire.RootElement.EnumerateObject().Select(item => item.Name).Should().Equal("schemaVersion", "context", "messages");
    }

    [Theory]
    [InlineData("text")]
    [InlineData("missing-answer")]
    [InlineData("wrong-answer-kind")]
    [InlineData("foreign-question")]
    [InlineData("repeat")]
    [InlineData("unsolicited-answer")]
    [InlineData("interleaved-result")]
    [InlineData("reused-call")]
    [InlineData("wrong-round")]
    [InlineData("no-opt-in")]
    public void ReadOnlyHistoryCannotInventAnswersSkipResultsOrResetCorrelationAtAQuestion(string fault)
    {
        var provenance = Provenance();
        var context = ModelToolProtocol.ReadContext(Envelope(provenance).Serialize(), provenance);
        var question = new ModelClarificationQuestion(new("Choose observation", fault is "text" ? [] : ["Version", "Readiness"]));
        var other = new ModelClarificationQuestion(new("Choose observation", ["Version", "Readiness"]));
        var answer = new ModelQuestionAnswerMessage(fault is "foreign-question" ? other : question,
            fault is "text" ? new([], "Version") : new(["option-0"]), RequestOrigin.LocalUi);
        var calls = Calls(Tool("one"));
        var result = ModelToolProtocol.CreateToolResult(calls.Calls[0],
            new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "recorded")));
        ModelToolMessage[] history = fault switch
        {
            "missing-answer" => [question],
            "wrong-answer-kind" => [question, calls, result],
            "repeat" => [question, answer, question, answer],
            "unsolicited-answer" => [answer],
            "interleaved-result" => [calls, question, answer, result],
            "reused-call" => [calls, result, question, answer, calls, result],
            _ => [question, answer],
        };
        var create = () => ModelToolProtocol.CreateRequest(provenance, null, context, history,
            fault is "wrong-round" ? 1 : history.Count(message => message is ModelAssistantMessage) + 1,
            clarificationAllowed: fault is not "no-opt-in");
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(ModelToolMode.LocalWebPage, ModelProviderIdentity.Ollama)]
    [InlineData(ModelToolMode.SessionDiscovery, ModelProviderIdentity.Ollama)]
    [InlineData(ModelToolMode.ReadOnly, ModelProviderIdentity.Copilot)]
    [InlineData(ModelToolMode.None, ModelProviderIdentity.Copilot)]
    public void ClarificationNeverWidensAnotherModeOrHostedSelection(ModelToolMode mode, ModelProviderIdentity provider)
    {
        ModelToolProtocol.AllowsClarification(mode, provider).Should().BeFalse();
        var provenance = Provenance();
        var envelope = Envelope(provenance);
        var context = ModelToolProtocol.ReadContext(new ModelContextEnvelope(provenance.Request, envelope.SystemPolicy,
            envelope.UserRequest, [], envelope.CreatedAt, envelope.ExpiresAt, mode).Serialize(), provenance);
        if (provider != ModelProviderIdentity.Ollama) { return; }
        var create = () => ModelToolProtocol.CreateRequest(provenance, null, context, [], 1, clarificationAllowed: true);
        create.Should().Throw<InvalidDataException>();
    }
}
