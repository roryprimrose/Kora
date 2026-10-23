using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    private const string StructuredQuestion = """{"kind":"question","prompt":"Which format?","options":["Compact","Detailed"]}""";

    [Fact]
    public async Task ExplicitQuestionProfileUsesStrictStructuredFormatAndOneExactAnswerWithoutAnyToolDeclaration()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Chat = (message, _) => Task.FromResult(Json(message, StructuredResponse(f.Bodies.Count == 1
            ? StructuredQuestion : """{"kind":"answer","answer":"Actual final answer"}""")));
        var first = f.Request(toolMode: ModelToolMode.None, clarificationAllowed: true);
        var reply = await f.Transport.CompleteAsync(first, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var question = reply.Message.Should().BeOfType<ModelClarificationQuestion>().Subject;
        var answer = new ModelQuestionAnswerMessage(question, new(["option-1"]), RequestOrigin.ActivatedVoice);
        var second = ModelToolProtocol.CreateRequest(first.Provenance, first.Profile, first.Context,
            [question, answer], 2, clarificationAllowed: true);
        var completed = await f.Transport.CompleteAsync(second, Token);
        completed.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        completed.Message.Should().BeOfType<ModelFinalResponse>().Which.Answer.Should().Be("Actual final answer");
        f.Bodies.Should().HaveCount(2);
        using var initial = JsonDocument.Parse(f.Bodies[0]);
        initial.RootElement.TryGetProperty("tools", out _).Should().BeFalse();
        initial.RootElement.GetProperty("format").GetProperty("oneOf").GetArrayLength().Should().Be(2);
        using var continued = JsonDocument.Parse(f.Bodies[1]);
        var messages = continued.RootElement.GetProperty("messages");
        messages.GetArrayLength().Should().Be(4);
        messages[2].GetProperty("role").GetString().Should().Be("assistant");
        messages[3].GetProperty("role").GetString().Should().Be("user");
        messages[3].GetProperty("content").GetString().Should().Contain("option-1").And.Contain("ActivatedVoice");
        (await f.Transport.CompleteAsync(second, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        f.Bodies.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("foreign-question")]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("cancelled")]
    [InlineData("profile")]
    public async Task QuestionNativeContinuationRejectsChangedIdentityOrAuthorityBeforeSecondExchange(string fault)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Chat = (message, _) => Task.FromResult(Json(message, StructuredResponse(StructuredQuestion)));
        var first = f.Request(toolMode: ModelToolMode.None, clarificationAllowed: true);
        var reply = await f.Transport.CompleteAsync(first, Token);
        var question = reply.Message.Should().BeOfType<ModelClarificationQuestion>().Subject;
        if (fault is "foreign-question") { question = new(new("Different", ["Alpha", "Beta"])); }
        var answer = new ModelQuestionAnswerMessage(question, new(["option-0"]), RequestOrigin.LocalUi);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "revoked") { await f.Revoked.CancelAsync(); }
        if (fault is "expired") { f.Time.Now = f.Time.Now.AddSeconds(15); }
        if (fault is "cancelled") { cancellation.Cancel(); }
        var second = ModelToolProtocol.CreateRequest(first.Provenance,
            fault is "profile" ? first.Profile! with { Model = new("foreign") } : first.Profile, first.Context,
            [question, answer], 2, clarificationAllowed: true);
        var result = await f.Transport.CompleteAsync(second, cancellation.Token);
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Message.Should().BeNull();
        f.Bodies.Should().ContainSingle();
    }

    [Theory]
    [InlineData("ordinary-prose")]
    [InlineData("question-and-answer")]
    [InlineData("credential")]
    [InlineData("tool")]
    [InlineData("unknown-field")]
    [InlineData("multiselect")]
    public async Task QuestionProfileNeverReinterpretsOrdinaryProseOrMixedAuthorityAsStructuredClarification(string fault)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var content = fault switch
        {
            "ordinary-prose" => "Which option would you like? Say yes to approve.",
            "question-and-answer" => """{"kind":"question","prompt":"Choose","options":["Alpha","Beta"],"answer":"already done"}""",
            "credential" => """{"kind":"question","prompt":"api_key=synthetic-only-value","options":[]}""",
            "unknown-field" => """{"kind":"question","prompt":"Choose","options":["Alpha","Beta"],"approved":true}""",
            "multiselect" => """{"kind":"question","prompt":"Choose","options":["Alpha","Beta"],"maximum":2}""",
            _ => StructuredQuestion,
        };
        f.Chat = (message, _) => Task.FromResult(Json(message, fault is "tool" ? Call : StructuredResponse(content)));
        var reply = await f.Transport.CompleteAsync(f.Request(toolMode: ModelToolMode.None, clarificationAllowed: true), Token);
        reply.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        reply.Message.Should().BeNull();
        f.Bodies.Should().ContainSingle();
    }

    private static string StructuredResponse(string content) =>
        JsonSerializer.Serialize(new { model = Model, message = new { role = "assistant", content }, done = true });
}
