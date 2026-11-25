using AwesomeAssertions;
using Kora.Core.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class CopilotSdkTurnRuntimeTests
{
    [Fact]
    public async Task Tool_free_sdk_streams_only_after_reviewed_transmission_and_preserves_final_answer()
    {
        var fixture = new Fixture { ToolMode = ModelToolMode.None };
        var answers = new List<string>();
        fixture.Client.Stream = async (publish, token) =>
        {
            fixture.Network.Sends.Should().Be(1);
            await publish("genuine", token);
            await publish("genuine answer", token);
            return "genuine answer";
        };
        using var root = fixture.Root();
        var reply = await fixture.Runtime.RunWithAnswerAsync(fixture.Provenance, fixture.Profile, fixture.Context,
            fixture.Tool, (_, _) => ValueTask.FromResult(true), (update, _) =>
            {
                update.Provenance.Should().BeSameAs(fixture.Provenance);
                answers.Add(update.Answer);
                return ValueTask.CompletedTask;
            }, Token);
        reply.Should().Be(new CopilotTurnReply(ModelTurnOutcome.Succeeded, "genuine answer"));
        answers.Should().Equal("genuine", "genuine answer");
        fixture.Client.Configuration!.Events.Streaming.Should().BeTrue();
        fixture.Client.Configuration.Tools.Definitions.Should().BeEmpty();
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("no-transmission")]
    [InlineData("revoked-account")]
    [InlineData("invalid-content")]
    [InlineData("nonmonotonic")]
    [InlineData("different-final")]
    public async Task Invalid_or_revoked_stream_never_publishes_an_unadmitted_prefix_or_returns_success(string fault)
    {
        var fixture = new Fixture { ToolMode = ModelToolMode.None, BypassTransmission = fault is "no-transmission" };
        var answers = new List<string>();
        fixture.Client.Stream = async (publish, token) =>
        {
            if (fault is "revoked-account") { fixture.AccountAvailable = false; }
            await publish(fault is "invalid-content" ? "answer\0" : "answer", token);
            if (fault is "nonmonotonic") { await publish("replacement", token); }
            return fault is "different-final" ? "other final" : "answer";
        };
        using var root = fixture.Root();
        var reply = await fixture.Runtime.RunWithAnswerAsync(fixture.Provenance, fixture.Profile, fixture.Context,
            fixture.Tool, (_, _) => ValueTask.FromResult(true), (update, _) =>
            {
                answers.Add(update.Answer);
                return ValueTask.CompletedTask;
            }, Token);
        reply.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        reply.Answer.Should().BeNull();
        answers.Should().Equal(fault is "nonmonotonic" or "different-final" ? ["answer"] : []);
        fixture.Client.Stops.Should().Be(1);
        fixture.Client.Disposals.Should().Be(1);
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Streaming_cannot_enable_tools_or_construct_a_client_for_a_tool_bearing_turn()
    {
        var fixture = new Fixture();
        using var root = fixture.Root();
        var result = await fixture.Runtime.RunWithAnswerAsync(fixture.Provenance, fixture.Profile, fixture.Context,
            fixture.Tool, (_, _) => ValueTask.FromResult(true),
            (_, _) => throw new InvalidOperationException("No provisional answer is admitted"), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        fixture.Options.Should().BeNull();
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }
}
