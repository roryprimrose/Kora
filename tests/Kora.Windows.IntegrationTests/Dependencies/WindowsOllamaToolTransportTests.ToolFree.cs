using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Agents;
using Kora.Windows.Dependencies;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Fact]
    public async Task AgentProposalUsesExactSdkSchemaWithoutToolsAndPreservesCompleteLiteralAnswer()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        const string body = "  Literal e\u0301\U0001f642\r\ntools: invented\n";
        var answer = JsonSerializer.Serialize(new { description = "Explain supplied text", body });
        f.Chat = (message, _) => Task.FromResult(Json(message, JsonSerializer.Serialize(new
        {
            model = Model, done = true, message = new { role = "assistant", content = answer },
        })));
        var request = f.Request(toolMode: ModelToolMode.None, outputFormat: ModelOutputFormat.OwnedAgentProposal);
        var reply = await f.Transport.CompleteAsync(request, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var final = reply.Message.Should().BeOfType<ModelFinalResponse>().Subject;
        final.Answer.Should().Be(answer);
        var proposal = OwnedAgentProfileDocument.ParseProposal("example", "", final.Answer);
        proposal.Document.Body.Should().Be(body);
        proposal.Profile.Tools.Should().BeEmpty();
        using var sent = JsonDocument.Parse(f.Bodies.Should().ContainSingle().Subject);
        var format = sent.RootElement.GetProperty("format");
        JsonElement.DeepEquals(format, OwnedAgentProfileDocument.ProposalSchema).Should().BeTrue();
        format.GetProperty("required").EnumerateArray().Select(item => item.GetString()).Should().Equal("description", "body");
        format.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        format.GetProperty("properties").GetProperty("body").GetProperty("type").GetString().Should().Be("string");
        sent.RootElement.TryGetProperty("tools", out _).Should().BeFalse();
        sent.RootElement.GetProperty("think").GetBoolean().Should().BeFalse();
        sent.RootElement.GetProperty("truncate").GetBoolean().Should().BeFalse();
        sent.RootElement.GetProperty("shift").GetBoolean().Should().BeFalse();
        reply.Usage!.InputUtf8Bytes.Should().BeGreaterThanOrEqualTo(f.Bodies[0].Length);
        (await f.Transport.CompleteAsync(request, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        f.Bodies.Should().ContainSingle();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("json")]
    [InlineData("different")]
    public async Task SdkCannotSilentlyIgnoreOrReplaceTheHostProposalSchema(string change)
    {
        var sends = 0;
        using var client = new HttpClient(new OllamaSdkRequestHandler(Model, false, (_, _) =>
        {
            sends++;
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }, outputSchema: OwnedAgentProfileDocument.ProposalSchema));
        var format = change switch { "missing" => "", "json" => ",\"format\":\"json\"", _ => ",\"format\":{}" };
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(WindowsOllamaToolTransport.Endpoint, "api/chat"))
        {
            Content = new StringContent($$"""{"model":"{{Model}}","messages":[],"stream":false,"think":false{{format}}}"""),
        };
        await ((Func<Task>)(async () => await client.SendAsync(message, Token))).Should().ThrowAsync<InvalidDataException>();
        sends.Should().Be(0);
    }

    [Fact]
    public async Task AgentProposalStreamingIsExplicitlyUnavailableWithoutBufferedFallback()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var updates = 0;
        var reply = await f.Transport.CompleteWithAnswerAsync(
            f.Request(toolMode: ModelToolMode.None, outputFormat: ModelOutputFormat.OwnedAgentProposal),
            (_, _) => { updates++; return ValueTask.CompletedTask; }, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        reply.Message.Should().BeNull();
        updates.Should().Be(0);
        f.Paths.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeToolFreeRequestOmitsEveryDeclarationAndRejectsUnsolicitedCallsWithoutContinuation(bool providerCallsTool)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message, providerCallsTool ? Call : Final));
        var request = fixture.Request(toolMode: ModelToolMode.None);
        var reply = await fixture.Transport.CompleteAsync(request, Token);
        reply.Outcome.Should().Be(providerCallsTool ? ModelTurnOutcome.Denied : ModelTurnOutcome.Succeeded);
        using var body = JsonDocument.Parse(fixture.Bodies.Should().ContainSingle().Subject);
        body.RootElement.TryGetProperty("tools", out _).Should().BeFalse();
        body.RootElement.GetProperty("messages").GetArrayLength().Should().Be(2);
        if (providerCallsTool) { reply.Message.Should().BeNull(); }
        else { reply.Message.Should().BeOfType<ModelFinalResponse>().Which.Answer.Should().Be("Genuine answer"); }
        (await fixture.Transport.CompleteAsync(request, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        fixture.Bodies.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAdapterCompositionCannotDispatchAnyToolInAHostCapturedToolFreeTurn(bool providerCallsTool)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message, providerCallsTool ? Call : Final));
        await using var adapter = new BoundedModelTurnAdapter(fixture.Transport, fixture.Time,
            NullLogger<BoundedModelTurnAdapter>.Instance);
        var invocations = 0;
        var reply = await adapter.RunAsync(fixture.Provenance, fixture.Profile,
            fixture.Request(toolMode: ModelToolMode.None).Context.Wire.ToArray(),
            (_, _, _) =>
            {
                invocations++;
                return ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Succeeded, "not-invoked"));
            }, token => ValidateContinuation(fixture, token), Token);
        reply.Outcome.Should().Be(providerCallsTool ? ModelTurnOutcome.Denied : ModelTurnOutcome.Succeeded);
        invocations.Should().Be(0);
        fixture.Bodies.Should().ContainSingle();
    }
}
