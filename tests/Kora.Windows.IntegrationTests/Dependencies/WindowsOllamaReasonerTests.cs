using System.Net;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class WindowsOllamaReasonerTests
{
    private static readonly LocalModelContext Context = new(
        "Kora",
        false,
        null,
        [new LocalModelDependency("Local inference", DependencyReadiness.Ready)],
        [new LocalModelTask("Setup", SetupTaskState.NeedsAction, null)]);

    [Fact]
    public async Task ReasonAsync_uses_only_the_request_on_the_pinned_loopback_model()
    {
        var calls = new List<string>();
        string? sentPrompt = null;
        using var client = new HttpClient(new StubHandler(async (message, token) =>
        {
            message.RequestUri!.Host.Should().Be("127.0.0.1");
            calls.Add(message.RequestUri.AbsolutePath);
            if (string.Equals(message.RequestUri.AbsolutePath, "/api/tags", StringComparison.Ordinal))
            {
                return Json("""{"models":[{"name":"qwen3:1.7b","digest":"8f68893c685c3ddff2aa3fffce2aa60a30bb2da65ca488b61fff134a4d1730e7"}]}""");
            }

            using var request = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(token));
            var root = request.RootElement;
            sentPrompt = root.GetProperty("prompt").GetString();
            root.GetProperty("model").GetString().Should().Be(WindowsOllamaSetupService.Model);
            root.GetProperty("stream").GetBoolean().Should().BeFalse();
            root.GetProperty("options").GetProperty("num_predict").GetInt32().Should().Be(512);
            root.GetProperty("format").GetString().Should().Be("json");
            root.GetProperty("think").GetBoolean().Should().BeFalse();
            var system = root.GetProperty("system").GetString();
            system.Should().Contain("\"AssistantName\":\"Kora\"");
            system.Should().Contain("\"Readiness\":\"Ready\"");
            system.Should().Contain("\"State\":\"NeedsAction\"");
            system.Should().NotContain("Why is the sky blue?");
            foreach (var command in new BuiltInCommandCatalog().GetCommands())
            {
                system.Should().Contain($"{command.Action}: ");
            }
            root.TryGetProperty("tools", out _).Should().BeFalse();
            root.TryGetProperty("messages", out _).Should().BeFalse();
            return Decision("""{"answer":"  The local answer.  "}""");
        }));

        var answer = await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
            .ReasonAsync("Why is the sky blue?", Context, TestContext.Current.CancellationToken);

        answer.Answer.Should().Be("The local answer.");
        answer.Action.Should().BeNull();
        sentPrompt.Should().Be("Why is the sky blue?");
        calls.Should().Equal("/api/tags", "/api/generate");
    }

    [Fact]
    public async Task ReasonAsync_rejects_changed_model_without_sending_user_text()
    {
        var calls = new List<string>();
        using var client = new HttpClient(new StubHandler((message, _) =>
        {
            calls.Add(message.RequestUri!.AbsolutePath);
            return Task.FromResult(Json("""{"models":[{"name":"qwen3:1.7b","digest":"sha256:changed"}]}"""));
        }));

        var action = () => new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
            .ReasonAsync("private question", Context, TestContext.Current.CancellationToken);

        (await action.Should().ThrowAsync<InvalidDataException>())
            .WithMessage("*unexpected digest*");
        calls.Should().ContainSingle().Which.Should().Be("/api/tags");
    }

    [Fact]
    public async Task ReasonAsync_rejects_incomplete_generation()
    {
        using var client = new HttpClient(new StubHandler((message, _) =>
            Task.FromResult(string.Equals(message.RequestUri!.AbsolutePath, "/api/tags", StringComparison.Ordinal)
                ? Json($$"""{"models":[{"name":"qwen3:1.7b","digest":"{{WindowsOllamaSetupService.ModelDigest}}"}]}""")
                : Json("""{"model":"qwen3:1.7b","done":false,"response":"partial"}"""))));

        var action = () => new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
            .ReasonAsync("question", Context, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ReasonAsync_accepts_only_registered_action_identifiers()
    {
        using var client = new HttpClient(new StubHandler((message, _) =>
            Task.FromResult(string.Equals(message.RequestUri!.AbsolutePath, "/api/tags", StringComparison.Ordinal)
                ? Json($$"""{"models":[{"name":"qwen3:1.7b","digest":"{{WindowsOllamaSetupService.ModelDigest}}"}]}""")
                : Decision("""{"action":"ShowStatus"}"""))));

        var result = await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
            .ReasonAsync("show me your status", Context, TestContext.Current.CancellationToken);

        result.Action.Should().Be(BuiltInAction.ShowStatus);
        result.Answer.Should().BeNull();
    }

    [Fact]
    public async Task ReasonAsync_accepts_a_grant_change_without_an_executable_action()
    {
        using var client = new HttpClient(new StubHandler((message, _) =>
            Task.FromResult(string.Equals(message.RequestUri!.AbsolutePath, "/api/tags", StringComparison.Ordinal)
                ? Json($$"""{"models":[{"name":"qwen3:1.7b","digest":"{{WindowsOllamaSetupService.ModelDigest}}"}]}""")
                : Decision("""{"grantChange":{"operation":"Remove","action":"LockMachine","scope":"Always"}}"""))));

        var result = await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
            .ReasonAsync("remove my always lock grant", Context, TestContext.Current.CancellationToken);

        result.GrantChange.Should().Be(new GrantChange(
            GrantChangeOperation.Remove, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        result.Action.Should().BeNull();
    }

    [Fact]
    public async Task ReasonAsync_accepts_a_bounded_clarification_question()
    {
        using var client = new HttpClient(new StubHandler((message, _) =>
            Task.FromResult(string.Equals(message.RequestUri!.AbsolutePath, "/api/tags", StringComparison.Ordinal)
                ? Json($$"""{"models":[{"name":"qwen3:1.7b","digest":"{{WindowsOllamaSetupService.ModelDigest}}"}]}""")
                : Decision("""{"question":{"prompt":"Which report?","options":["Today","Yesterday"]}}"""))));

        var result = await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
            .ReasonAsync("show a report", Context, TestContext.Current.CancellationToken);

        result.Question!.Prompt.Should().Be("Which report?");
        result.Question.Options.Should().Equal("Today", "Yesterday");
        result.Action.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"action":"InstallLocalModel"}""")]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"action":"LockMachine","answer":"already done"}""")]
    [InlineData("""{"action":"lock the machine"}""")]
    [InlineData("""{"answer":""}""")]
    [InlineData("""{"grantChange":{"operation":"Remove","action":"LockMachine","scope":"Once"}}""")]
    [InlineData("""{"grantChange":{"operation":"Add","action":"LockMachine"}}""")]
    [InlineData("""{"grantChange":{"operation":"Move","action":"LockMachine","scope":"Always"}}""")]
    [InlineData("""{"grantChange":{"operation":"Remove","action":"InstallLocalModel","scope":"Always"}}""")]
    [InlineData("""{"grantChange":{"operation":"Remove","action":"LockMachine","scope":"Always","extra":"x"}}""")]
    [InlineData("""{"grantChange":{"operation":"Remove","action":"LockMachine","scope":"Always","scope":"Always"}}""")]
    [InlineData("""{"grantChange":{"operation":"Remove","action":"LockMachine","scope":"Always"},"answer":"done"}""")]
    [InlineData("""{"question":{"prompt":"Choose?","options":["Only one"]}}""")]
    [InlineData("""{"question":{"prompt":"Choose?","options":["A","a"]}}""")]
    [InlineData("""{"question":{"prompt":"Choose?","options":["two","one"]}}""")]
    [InlineData("""{"question":{"prompt":"Choose?","options":["A","B"],"action":"LockMachine"}}""")]
    [InlineData("""{"question":{"prompt":"Choose?","options":["A","B"]},"action":"LockMachine"}""")]
    [InlineData("""{"question":{"prompt":"Choose?","options":["A",2]}}""")]
    public async Task ReasonAsync_rejects_unauthorized_or_ambiguous_model_decisions(string decision)
    {
        using var client = new HttpClient(new StubHandler((message, _) =>
            Task.FromResult(string.Equals(message.RequestUri!.AbsolutePath, "/api/tags", StringComparison.Ordinal)
                ? Json($$"""{"models":[{"name":"qwen3:1.7b","digest":"{{WindowsOllamaSetupService.ModelDigest}}"}]}""")
                : Decision(decision))));

        var action = () => new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
            .ReasonAsync("private question", Context, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ReasonAsync_rejects_oversized_request_before_contacting_ollama()
    {
        using var client = new HttpClient(new StubHandler((_, _) =>
            throw new InvalidOperationException("No network request expected.")));

        var action = () => new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
            .ReasonAsync(new string('a', 4097), Context, TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ReasonAsync_honors_cancellation_without_sending_a_request()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var client = new HttpClient(new StubHandler((_, token) =>
            Task.FromCanceled<HttpResponseMessage>(token)));

        var action = () => new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
            .ReasonAsync("question", Context, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private static HttpResponseMessage Decision(string decision) =>
        Json(JsonSerializer.Serialize(new
        {
            model = WindowsOllamaSetupService.Model,
            done = true,
            response = decision,
        }));

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
