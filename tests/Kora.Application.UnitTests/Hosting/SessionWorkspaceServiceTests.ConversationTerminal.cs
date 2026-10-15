using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task TerminalPredicatesComposeWithRoleQueryAndPendingRowsWithoutWrites()
    {
        using var f = new ConversationFixture();
        f.Messages = [Message(f, 1, "marker"), Message(f, 2, "marker") with { Outcome = ModelTurnOutcome.Cancelled,
            Reason = ModelTurnReason.CallerCancelled }, Message(f, 3, "marker Unavailable UnregisteredModel") with
            { Revision = 1, Outcome = null, Reason = null }];
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        foreach (var role in Enum.GetValues<ConversationSearchRole>())
        {
            foreach (var filter in new[] { default, new ConversationTerminalFilter(ModelTurnOutcome.Unavailable),
                new(null, ModelTurnReason.UnregisteredModel), new(null, null, HostTaskState.Unavailable),
                new(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel, HostTaskState.Unavailable),
                new(ModelTurnOutcome.Unavailable, ModelTurnReason.CallerCancelled),
                new(null, null, HostTaskState.IntentRecorded) })
            {
                var page = await f.Service.SearchConversationAsync(f.Session.SessionId, "marker", null, 25, f.Token, role, filter);
                var expected = role == ConversationSearchRole.HOST ? 0
                    : filter == default ? 3 : role == ConversationSearchRole.USER
                        || filter.Reason == ModelTurnReason.CallerCancelled || filter.TaskState == HostTaskState.IntentRecorded ? 0 : 1;
                page.Messages.Should().HaveCount(expected);
                page.Terminal.Should().Be(filter);
                page.Role.Should().Be(role);
                page.Scanned.Should().Be(3);
                page.Next.Should().BeNull();
            }
        }
        var selected = new ConversationTerminalFilter(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled, HostTaskState.Cancelled);
        (await f.Service.SearchConversationAsync(f.Session.SessionId, "Cancelled", null, 25, f.Token,
            ConversationSearchRole.HOST, selected)).Messages.Single().Should().Be(f.Messages[1]);
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Fact]
    public async Task TerminalCursorTamperingOrUndefinedInputsFailBeforeReading()
    {
        using var f = new ConversationFixture();
        f.Messages = [Message(f, 1, "marker"), Message(f, 2, "marker")];
        var filter = new ConversationTerminalFilter(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel, HostTaskState.Unavailable);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var first = await f.Service.SearchConversationAsync(f.Session.SessionId, "marker", null, 1, f.Token,
            ConversationSearchRole.All, filter);
        first.Next!.Terminal.Should().Be(filter);
        var reads = f.Reads;
        foreach (var altered in new[] { default, filter with { Outcome = null }, filter with { Reason = null },
            filter with { TaskState = null }, filter with { Outcome = ModelTurnOutcome.Denied },
            filter with { Reason = ModelTurnReason.None }, filter with { TaskState = HostTaskState.Denied },
            filter with { Outcome = (ModelTurnOutcome)99 }, filter with { Reason = (ModelTurnReason)99 },
            filter with { TaskState = (HostTaskState)99 } })
        {
            await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId,
                "marker", first.Next with { Terminal = altered }, 1, f.Token, ConversationSearchRole.All, filter))
                .Should().ThrowAsync<ArgumentException>();
            await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId,
                "marker", first.Next, 1, f.Token, ConversationSearchRole.All, altered))
                .Should().ThrowAsync<ArgumentException>();
        }
        f.Reads.Should().Be(reads);
        var next = await f.Service.SearchConversationAsync(f.Session.SessionId, "MARKER", first.Next, 1, f.Token,
            ConversationSearchRole.All, filter);
        next.Messages.Single().Should().Be(f.Messages[1]);
        next.Terminal.Should().Be(filter);
        next.Next.Should().BeNull();
    }

    [Theory]
    [InlineData("body")]
    [InlineData("outcome")]
    [InlineData("reason")]
    [InlineData("revision")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("sequence")]
    public async Task UserOnlyZeroStillValidatesEveryScannedRowBeforeTerminalExclusion(string invalid)
    {
        using var f = new ConversationFixture();
        var message = Message(f, 2, "marker");
        message = invalid switch
        {
            "body" => message with { Body = "\ud800" },
            "outcome" => message with { Outcome = (ModelTurnOutcome)99 },
            "reason" => message with { Reason = (ModelTurnReason)99 },
            "revision" => message with { Revision = 1 },
            "session" => message with { Request = HostRequest.Create(RequestOrigin.LocalUi) },
            "generation" => message with { Generation = new(2) },
            _ => message with { Sequence = 3 },
        };
        f.Messages = [Message(f, 1, "marker"), message];
        f.AfterRead = page => page with { Messages = [.. f.Messages] };
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.SearchConversationAsync(f.Session.SessionId, "marker", null, 25,
            f.Token, ConversationSearchRole.USER, new(ModelTurnOutcome.Succeeded))).Should().ThrowAsync<InvalidDataException>();
        f.TaskWrites.Should().BeEmpty();
    }
}
