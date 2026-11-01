using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Theory]
    [InlineData(ModelToolMode.None, true, true)]
    [InlineData(ModelToolMode.None, false, false)]
    [InlineData(ModelToolMode.ReadOnly, true, false)]
    public async Task NativeFormsAreAdvertisedAndConsumedOnlyByExplicitLocalClarification(
        ModelToolMode mode, bool clarification, bool permitted)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        const string arguments = """
            {"prompt":"Describe the answer","fields":[
            {"label":"Audience","type":"text","maximumLength":100,"maximumUtf8Bytes":200},
            {"label":"Style","type":"singleChoice","options":["Compact","Detailed"]}]}
            """;
        f.Chat = (message, _) => Task.FromResult(Json(message, f.Bodies.Count == 1
            ? AskUserResponse(arguments) : StructuredResponse("""{"kind":"answer","answer":"Continued answer"}""")));
        var first = f.Request(toolMode: mode, clarificationAllowed: clarification);
        var reply = await f.Transport.CompleteAsync(first, Token);
        if (!permitted)
        {
            reply.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
            reply.Message.Should().BeNull();
            return;
        }
        var question = reply.Message.Should().BeOfType<ModelClarificationQuestion>().Subject;
        question.Question.Fields.Should().HaveCount(2);
        using var initial = JsonDocument.Parse(f.Bodies[0]);
        var tools = initial.RootElement.GetProperty("tools");
        tools.GetArrayLength().Should().Be(1);
        tools[0].GetProperty("function").GetProperty("parameters").GetProperty("oneOf")[1]
            .GetProperty("properties").GetProperty("fields").GetProperty("maxItems").GetInt32().Should().Be(4);
        var answer = new ModelQuestionAnswerMessage(question,
            new([], fields: [new("field-0", "Reviewers"), new("field-1", Choice: "option-1")]), RequestOrigin.LocalUi);
        var completed = await f.Transport.CompleteAsync(ModelToolProtocol.CreateRequest(first.Provenance, first.Profile,
            first.Context, [question, answer], 2, clarificationAllowed: true), Token);
        completed.Message.Should().BeOfType<ModelFinalResponse>().Which.Answer.Should().Be("Continued answer");
        using var continuation = JsonDocument.Parse(f.Bodies[1]);
        var messages = continuation.RootElement.GetProperty("messages");
        messages[2].GetProperty("tool_calls")[0].GetProperty("function").GetProperty("arguments")
            .GetProperty("fields").GetArrayLength().Should().Be(2);
        messages[3].GetProperty("content").GetString().Should().Be(System.Text.Encoding.UTF8.GetString(ModelToolProtocol.SerializeMessage(answer)));
        continuation.RootElement.GetProperty("tools").GetArrayLength().Should().Be(0);
    }
}
