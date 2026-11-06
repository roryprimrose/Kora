using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task NativeAskUserDeclaresAndResumesExactTextRadioOrCheckboxToolCall(bool readOnly, int selections)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var arguments = JsonSerializer.Serialize(new
        {
            prompt = "Which details?",
            options = selections == 0 ? Array.Empty<string>() : ["Version", "Readiness"],
            minimum = 1,
            maximum = Math.Max(1, selections),
        });
        f.Chat = (message, _) => Task.FromResult(Json(message, f.Bodies.Count == 1
            ? AskUserResponse(arguments) : StructuredResponse("""{"kind":"answer","answer":"Actual clarified answer"}""")));
        var first = f.Request(toolMode: readOnly ? ModelToolMode.ReadOnly : ModelToolMode.None, clarificationAllowed: true);
        var reply = await f.Transport.CompleteAsync(first, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var question = reply.Message.Should().BeOfType<ModelClarificationQuestion>().Subject;
        question.CallId.Should().Be("ask_1");
        var spec = LocalModelQuestionPolicy.CreateSpec(question.Question);
        spec.Kind.Should().Be(selections == 0 ? QuestionKind.Text : selections == 1 ? QuestionKind.SingleChoice : QuestionKind.MultipleChoice);
        var answer = new ModelQuestionAnswerMessage(question, selections == 0 ? new([], "Original user details")
            : new(spec.Options.Take(selections).Select(option => option.Id)), RequestOrigin.ActivatedVoice);
        var second = ModelToolProtocol.CreateRequest(first.Provenance, first.Profile, first.Context,
            [question, answer], 2, clarificationAllowed: true);
        var completed = await f.Transport.CompleteAsync(second, Token);
        completed.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        completed.Message.Should().BeOfType<ModelFinalResponse>().Which.Answer.Should().Be("Actual clarified answer");
        using var initial = JsonDocument.Parse(f.Bodies[0]);
        var tools = initial.RootElement.GetProperty("tools");
        tools.GetArrayLength().Should().Be(readOnly ? 7 : 1);
        var tool = tools[0].GetProperty("function");
        tool.GetProperty("name").GetString().Should().Be("ask_user");
        var parameters = tool.GetProperty("parameters");
        var legacy = readOnly ? parameters : parameters.GetProperty("oneOf")[0];
        legacy.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        legacy.GetProperty("required").GetArrayLength().Should().Be(4);
        using var continuation = JsonDocument.Parse(f.Bodies[1]);
        var messages = continuation.RootElement.GetProperty("messages");
        var call = messages[2].GetProperty("tool_calls")[0];
        call.GetProperty("id").GetString().Should().Be("ask_1");
        call.GetProperty("function").GetProperty("name").GetString().Should().Be("ask_user");
        call.GetProperty("function").GetProperty("arguments").GetProperty("maximum").GetInt32().Should().Be(Math.Max(1, selections));
        messages[3].GetProperty("role").GetString().Should().Be("tool");
        messages[3].GetProperty("tool_name").GetString().Should().Be("ask_user");
        messages[3].GetProperty("tool_call_id").GetString().Should().Be("ask_1");
        messages[3].GetProperty("content").GetString().Should().Be(System.Text.Encoding.UTF8.GetString(ModelToolProtocol.SerializeMessage(answer)));
        continuation.RootElement.GetProperty("tools").EnumerateArray().Should().NotContain(item =>
            item.GetProperty("function").GetProperty("name").GetString() == "ask_user");
    }

    [Theory]
    [InlineData("no-opt-in")]
    [InlineData("mixed-batch")]
    [InlineData("mixed-content")]
    [InlineData("unknown-field")]
    [InlineData("repeat")]
    [InlineData("invalid-bounds")]
    [InlineData("structured-tool")]
    public async Task NativeAskUserCannotAcquireAuthorityMixWorkOrRepeat(string fault)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var arguments = fault is "invalid-bounds"
            ? """{"prompt":"Choose","options":["A","B"],"minimum":2,"maximum":1}"""
            : """{"prompt":"Choose","options":["A","B"],"minimum":1,"maximum":2}""";
        var native = AskUserResponse(arguments, duplicate: fault is "mixed-batch", content: fault is "mixed-content" ? "Already answered" : "");
        if (fault is "structured-tool")
        { native = StructuredResponse("""{"kind":"question","prompt":"Choose","options":["A","B"],"minimum":1,"maximum":2,"callId":"ask_1"}"""); }
        if (fault is "unknown-field") { native = native.Replace("\"minimum\":1", "\"approved\":true,\"minimum\":1", StringComparison.Ordinal); }
        f.Chat = (message, _) => Task.FromResult(Json(message, native));
        var first = f.Request(clarificationAllowed: fault is not "no-opt-in");
        var reply = await f.Transport.CompleteAsync(first, Token);
        if (fault is "repeat")
        {
            var question = (ModelClarificationQuestion)reply.Message!;
            var answer = new ModelQuestionAnswerMessage(question, new(["option-0"]), RequestOrigin.LocalUi);
            reply = await f.Transport.CompleteAsync(ModelToolProtocol.CreateRequest(first.Provenance, first.Profile,
                first.Context, [question, answer], 2, clarificationAllowed: true), Token);
        }
        reply.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        reply.Message.Should().BeNull();
        f.Bodies.Should().HaveCount(fault is "repeat" ? 2 : 1);
    }

    private static string AskUserResponse(string arguments, bool duplicate = false, string content = "")
    {
        using var input = JsonDocument.Parse(arguments);
        var call = new { id = "ask_1", type = "function", function = new { name = "ask_user", arguments = input.RootElement } };
        return JsonSerializer.Serialize(new
        {
            model = Model,
            message = new { role = "assistant", content, tool_calls = duplicate ? new[] { call, call } : [call] },
            done = true,
        });
    }
}
