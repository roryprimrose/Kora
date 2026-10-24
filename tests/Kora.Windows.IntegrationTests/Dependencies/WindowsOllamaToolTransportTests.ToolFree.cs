using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
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
