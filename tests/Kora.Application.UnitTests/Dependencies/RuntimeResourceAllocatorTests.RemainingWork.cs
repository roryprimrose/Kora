using AwesomeAssertions;

using Kora.Application.UnitTests.Agents;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class RuntimeResourceAllocatorTests
{
    [Fact]
    public async Task RemainingWorkAnswersOneAddressedOrdinaryLaneWhileTwoIndependentTransportsStayHeld()
    {
        var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture(runtimeCapacity: source, registrationSource: source);
        using var cleanup = source;
        var one = f.Conversation.SubmitAsync(f.Sessions[0], "private first original", () => true, Token);
        await source.Started(0, f);
        var two = f.Conversation.SubmitAsync(f.Sessions[1], "private other original", () => true, Token);
        await source.Started(1, f);
        var pending = await f.Conversation.SubmitQueuedAsync(f.Sessions[0], "private first pending", () => true, Token);
        var audits = f.Audits.ToArray();
        var tasks = f.Tasks.ToArray();
        var messages = f.Messages.ToArray();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var answer = await f.Conversation.ReadRemainingWorkAsync(f.Sessions[0], () => true, Token);
        await f.Conversation.ValidateRemainingWorkAsync(answer, () => true, Token);
        answer.Format().Should().Contain("Current ordinary work: running").And.Contain("FIFO position 1")
            .And.NotContain("private").And.NotContain(f.Sessions[1].Authority.SessionId.Value.ToString("D"));
        answer.Ordinary.Running!.Target.Request.SessionId.Should().Be(f.Sessions[0].Authority.SessionId);
        answer.Unresolved.Should().OnlyContain(task => task.Request.SessionId == f.Sessions[0].Authority.SessionId);
        var other = await f.Conversation.ReadRemainingWorkAsync(f.Sessions[1], () => true, Token);
        other.Format().Should().Contain("Pending ordinary FIFO entries: 0");
        one.IsCompleted.Should().BeFalse();
        two.IsCompleted.Should().BeFalse();
        source.Transports.Should().HaveCount(2);
        f.Audits.Should().Equal(audits);
        f.Tasks.Should().BeEquivalentTo(tasks);
        f.Messages.Should().BeEquivalentTo(messages);
        var lane = await f.Conversation.ReadPendingConversationAsync(f.Sessions[0], () => true, Token);
        await f.Conversation.CancelPendingConversationAsync(f.Sessions[0],
            lane.Turns.Single(turn => turn.Submission == pending.Receipt), lane.Revision, () => true, Token);
        source.Transport(0, f).Finish("first answer");
        await one;
        two.IsCompleted.Should().BeFalse();
        source.Transport(1, f).Finish("second answer");
        await two;
    }
}