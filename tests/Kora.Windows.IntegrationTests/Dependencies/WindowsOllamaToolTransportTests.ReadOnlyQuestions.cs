using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CombinedNativeHistoryKeepsEveryToolAndExactQuestionAnswerInOriginalOrder(
        bool toolFirst, bool answerPublisher)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var questionRound = toolFirst ? 2 : 1;
        f.Chat = (message, _) => Task.FromResult(Json(message,
            f.Bodies.Count == questionRound ? StructuredResponse(StructuredQuestion)
            : f.Bodies.Count == questionRound + 2 ? StructuredResponse("""{"kind":"answer","answer":"Observed answer"}""") : Call));
        var original = f.Request(clarificationAllowed: true);
        var history = new List<ModelToolMessage>();
        var updates = new List<ModelAnswerUpdate>();
        var input = ModelToolProtocol.MaximumInputUtf8Bytes;
        var output = ModelToolProtocol.MaximumOutputUtf8Bytes;
        for (var round = 1; round <= questionRound + 2; round++)
        {
            var request = ModelToolProtocol.CreateRequest(original.Provenance, original.Profile, original.Context, history,
                round, new(input, output), clarificationAllowed: true);
            var reply = answerPublisher
                ? await f.Transport.CompleteWithAnswerAsync(request, (update, _) =>
                {
                    updates.Add(update);
                    return ValueTask.CompletedTask;
                }, Token)
                : await f.Transport.CompleteAsync(request, Token);
            reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            input -= reply.Usage!.InputUtf8Bytes;
            output -= reply.Usage.OutputUtf8Bytes;
            if (reply.Message is ModelAssistantToolCalls calls)
            {
                history.Add(calls);
                var result = ModelToolProtocol.CreateToolResult(calls.Calls[0], VersionReply());
                history.Add(result);
                output -= ModelToolProtocol.SerializeMessage(result).Length;
                calls.Calls[0].CallId.Should().Be($"ollama_r{round}_c0");
            }
            else if (reply.Message is ModelClarificationQuestion question)
            {
                history.Add(question);
                var answer = new ModelQuestionAnswerMessage(question, new(["option-1"]), RequestOrigin.ActivatedVoice);
                history.Add(answer);
                output -= ModelToolProtocol.SerializeMessage(answer).Length;
            }
            else { reply.Message.Should().BeOfType<ModelFinalResponse>().Which.Answer.Should().Be("Observed answer"); }
        }
        using var sent = JsonDocument.Parse(f.Bodies[^1]);
        var messages = sent.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        messages.Select(message => message.GetProperty("role").GetString()).Should().Equal(toolFirst
            ? ["system", "user", "assistant", "tool", "assistant", "user", "assistant", "tool"]
            : ["system", "user", "assistant", "user", "assistant", "tool"]);
        sent.RootElement.GetProperty("tools").GetArrayLength().Should().Be(6);
        sent.RootElement.GetProperty("format").GetProperty("oneOf")[1].GetProperty("properties")
            .GetProperty("options").GetProperty("minItems").GetInt32().Should().Be(2);
        updates.Should().BeEmpty("read-only clarification must not enter the no-tools provisional answer stream");
        foreach (var body in f.Bodies)
        {
            using var exchange = JsonDocument.Parse(body);
            exchange.RootElement.GetProperty("stream").GetBoolean().Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("question-clone")]
    [InlineData("answer-change")]
    [InlineData("prefix-result")]
    [InlineData("unobserved-call")]
    public async Task CombinedTransportRejectsHistoryTamperingBeforeAnotherPhysicalSend(string fault)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Chat = (message, _) => Task.FromResult(Json(message,
            f.Bodies.Count == 2 ? StructuredResponse(StructuredQuestion) : Call));
        var original = f.Request(clarificationAllowed: true);
        var first = await f.Transport.CompleteAsync(original, Token);
        var calls = (ModelAssistantToolCalls)first.Message!;
        var result = ModelToolProtocol.CreateToolResult(calls.Calls[0], VersionReply());
        var questionReply = await f.Transport.CompleteAsync(ModelToolProtocol.CreateRequest(original.Provenance, original.Profile,
            original.Context, [calls, result], 2, clarificationAllowed: true), Token);
        var question = (ModelClarificationQuestion)questionReply.Message!;
        if (fault is "question-clone") { question = new(question.Question); }
        var answer = new ModelQuestionAnswerMessage(question, new(["option-0"]), RequestOrigin.LocalUi);
        var history = new List<ModelToolMessage> { calls, result, question, answer };
        var round = 3;
        if (fault is "answer-change")
        {
            var third = await f.Transport.CompleteAsync(ModelToolProtocol.CreateRequest(original.Provenance, original.Profile,
                original.Context, history, round, clarificationAllowed: true), Token);
            var more = (ModelAssistantToolCalls)third.Message!;
            history[3] = new ModelQuestionAnswerMessage(question, new(["option-1"]), RequestOrigin.LocalUi);
            history.Add(more);
            history.Add(ModelToolProtocol.CreateToolResult(more.Calls[0], VersionReply()));
            round++;
        }
        if (fault is "prefix-result")
        { history[1] = ModelToolProtocol.CreateToolResult(calls.Calls[0], new(CapabilityOutcome.Succeeded, "changed", Version: new("9", "changed"))); }
        if (fault is "unobserved-call")
        {
            var unobserved = (ModelAssistantToolCalls)ModelToolProtocol.ParseAssistant(
                """{"kind":"toolCalls","calls":[{"callId":"unobserved","capabilityId":"application.get_version","input":{}}]}"""u8);
            history.Add(unobserved);
            history.Add(ModelToolProtocol.CreateToolResult(unobserved.Calls[0], VersionReply()));
            round++;
        }
        var sends = f.Bodies.Count;
        var denied = await f.Transport.CompleteAsync(ModelToolProtocol.CreateRequest(original.Provenance, original.Profile,
            original.Context, history, round, clarificationAllowed: true), Token);
        denied.Outcome.Should().Be(ModelTurnOutcome.Denied);
        denied.Message.Should().BeNull();
        f.Bodies.Should().HaveCount(sends);
    }

    [Theory]
    [InlineData("free-text")]
    [InlineData("mixed")]
    [InlineData("repeat")]
    public async Task CombinedNativeQuestionRemainsSingleChoiceAndCannotMixOrRepeat(string fault)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Chat = (message, _) => Task.FromResult(Json(message, fault switch
        {
            "free-text" => StructuredResponse("""{"kind":"question","prompt":"Choose","options":[]}"""),
            "mixed" => Call.Replace("\"content\":\"\"", "\"content\":" + JsonSerializer.Serialize(StructuredQuestion), StringComparison.Ordinal),
            _ => StructuredResponse(StructuredQuestion),
        }));
        var first = f.Request(clarificationAllowed: true);
        var reply = await f.Transport.CompleteAsync(first, Token);
        if (fault is "repeat")
        {
            var question = (ModelClarificationQuestion)reply.Message!;
            reply = await f.Transport.CompleteAsync(ModelToolProtocol.CreateRequest(first.Provenance, first.Profile, first.Context,
                [question, new ModelQuestionAnswerMessage(question, new(["option-0"]), RequestOrigin.LocalUi)], 2,
                clarificationAllowed: true), Token);
        }
        reply.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        reply.Message.Should().BeNull();
    }
}
