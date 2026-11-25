using AwesomeAssertions;
using Microsoft.Copilot;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class CopilotAnswerStreamTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Coalesced_deltas_preserve_complete_unicode_and_ignore_replayed_events()
    {
        var stream = new CopilotAnswerStream();
        var first = Delta("answer ");
        stream.Observe(first);
        stream.Observe(first);
        stream.Observe(Delta("\uD83D"));
        stream.Observe(Delta("\uDE00"));
        stream.Complete();
        var answers = new List<string>();
        await stream.PublishAsync((answer, _) =>
        {
            answers.Add(answer);
            return ValueTask.CompletedTask;
        }, Token);
        answers.Should().Equal("answer \uD83D\uDE00");
        stream.RequireFinal("answer \uD83D\uDE00");
        var different = () => stream.RequireFinal("other");
        different.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("foreign-message")]
    [InlineData("subagent")]
    [InlineData("tool")]
    [InlineData("unicode")]
    [InlineData("control")]
    [InlineData("bytes")]
    public async Task Invalid_delta_fails_publication_instead_of_flattening_or_truncating(string fault)
    {
        var stream = new CopilotAnswerStream();
        stream.Observe(Delta("prefix"));
        var delta = Delta(fault switch
        {
            "unicode" => "\uDE00",
            "control" => "\0",
            "bytes" => new string('\u20AC', 1400),
            _ => "next",
        });
        if (fault is "foreign-message") { delta.Data.MessageId = "foreign"; }
        if (fault is "subagent") { delta.AgentId = "subagent"; }
        if (fault is "tool") { delta.AgentId = "tool-origin-agent"; }
        stream.Observe(delta);
        stream.Complete();
        await Assert.ThrowsAsync<InvalidDataException>(() => stream.PublishAsync(
            (_, _) => ValueTask.CompletedTask, Token));
    }

    [Fact]
    public async Task Cancellation_does_not_drain_pending_content_into_a_retired_presentation()
    {
        var stream = new CopilotAnswerStream();
        stream.Observe(Delta("unpublished"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stream.PublishAsync(
            (_, _) => throw new InvalidOperationException("No cancelled content may publish"), cancellation.Token));
        stream.Complete();
    }

    private static AssistantMessageDeltaEvent Delta(string text) => new()
    {
        Id = Guid.NewGuid(),
        Data = new() { MessageId = "message-1", DeltaContent = text },
    };
}
