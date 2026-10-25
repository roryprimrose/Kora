using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    private static ConversationMessage SearchAnswer(ConversationFixture f, int sequence, string body = "saved café")
    {
        var original = Message(f, sequence, "original USER words");
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), original.Request, ModelProviderSelection.OllamaCandidate);
        return original with
        {
            Version = 2, Revision = 3, Execution = provenance, ExecutionMetadataRevision = 0,
            Outcome = ModelTurnOutcome.Succeeded, Reason = ModelTurnReason.ProviderOutcome,
            AssistantResponse = new(provenance, body)
        };
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task AssistantCommandPagesOriginalRoleAndVerbatimQueryWithoutSubmission(RequestOrigin origin, bool done)
    {
        using var f = new ConversationFixture();
        f.Session = f.Session with { IsActive = !done };
        f.Messages = Enumerable.Range(1, 30).Select(index => SearchAnswer(f, index)).ToArray();
        var clock = new SearchTime();
        var service = SearchCommands(f, clock);
        var command = SessionCommand.Parse("Nova, conversation search " + f.Session.SessionId.Value.ToString("D")
            + " --role ASSISTANT --   CAFE\u0301  ", "Nova")!;
        var first = (await service.ExecuteAsync(command, origin, static () => true, f.Token))!;
        first.Messages.Should().Equal(f.Messages.Take(25));
        first.Role.Should().Be(ConversationSearchRole.ASSISTANT);
        first.QueryDigest.Should().Be(new ConversationSearch("café", ConversationSearchRole.ASSISTANT).Digest);
        clock.Now = clock.Now.AddSeconds(59);
        var next = (await service.ExecuteAsync(SearchNext(f) with
        {
            ConversationQuery = "replacement ignored", ConversationRole = (ConversationSearchRole)99
        }, origin, static () => true, f.Token))!;
        next.Messages.Should().Equal(f.Messages.Skip(25));
        next.Role.Should().Be(first.Role);
        next.Next.Should().BeNull();
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Fact]
    public async Task AssistantCommandContinuationKeepsOriginalExpiryAndCanBeCancelled()
    {
        using var f = new ConversationFixture();
        f.Messages = Enumerable.Range(1, 60).Select(index => SearchAnswer(f, index)).ToArray();
        var clock = new SearchTime();
        var service = SearchCommands(f, clock);
        var command = SearchCommand(f, "café") with { ConversationRole = ConversationSearchRole.ASSISTANT };
        await service.ExecuteAsync(command, RequestOrigin.ActivatedVoice, static () => true, f.Token);
        clock.Now = clock.Now.AddSeconds(59);
        (await service.ExecuteAsync(SearchNext(f), RequestOrigin.ActivatedVoice, static () => true, f.Token))!.Next.Should().NotBeNull();
        clock.Now = clock.Now.AddSeconds(1);
        var reads = f.Reads;
        await service.Invoking(value => value.ExecuteAsync(SearchNext(f), RequestOrigin.ActivatedVoice, static () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Reads.Should().Be(reads);
        await service.ExecuteAsync(command, RequestOrigin.ActivatedVoice, static () => true, f.Token);
        (await service.ExecuteAsync(SearchNext(f) with { Operation = SessionCommandOperation.ConversationSearchCancel },
            RequestOrigin.ActivatedVoice, static () => true, f.Token)).Should().BeNull();
        await service.Invoking(value => value.ExecuteAsync(SearchNext(f), RequestOrigin.ActivatedVoice, static () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AssistantPagesPreserveWholeAnswersWithinCompleteOutputBudgetAndRejectRoleTampering()
    {
        using var f = new ConversationFixture();
        f.Messages = Enumerable.Range(1, 8).Select(index => SearchAnswer(f, index, "saved " + new string('a', 3500))
            with { Body = new string('\u0800', 3000) }).ToArray();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        ConversationSearchCursor? cursor = null;
        var messages = new List<ConversationMessage>();
        do
        {
            var page = await f.Service.SearchConversationAsync(f.Session.SessionId, "saved", cursor, 50, f.Token,
                ConversationSearchRole.ASSISTANT);
            page.Messages.Should().NotBeEmpty();
            ConversationSearchPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
            messages.AddRange(page.Messages);
            cursor = page.Next;
            if (cursor is not null)
            {
                var reads = f.Reads;
                await f.Service.Invoking(value => value.SearchConversationAsync(f.Session.SessionId, "saved",
                    cursor with { Role = ConversationSearchRole.All }, 50, f.Token, ConversationSearchRole.ASSISTANT))
                    .Should().ThrowAsync<ArgumentException>();
                await f.Service.Invoking(value => value.SearchConversationAsync(f.Session.SessionId, "saved",
                    cursor with { QueryDigest = new ConversationSearch("saved").Digest }, 50, f.Token, ConversationSearchRole.ASSISTANT))
                    .Should().ThrowAsync<ArgumentException>();
                f.Reads.Should().Be(reads);
            }
        } while (cursor is not null);
        messages.Should().Equal(f.Messages);
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("malformed-answer")]
    [InlineData("malformed-user")]
    [InlineData("snapshot")]
    [InlineData("cancel")]
    public async Task AssistantSearchRefusesMalformedExcludedRowsAndChangedSnapshots(string fault)
    {
        using var f = new ConversationFixture();
        var answer = SearchAnswer(f, 1);
        var excluded = Message(f, 2, "no answer");
        f.Messages = [answer, excluded];
        using var cancellation = new CancellationTokenSource();
        f.AfterRead = page =>
        {
            if (fault is "cancel") { cancellation.Cancel(); }
            return fault switch
            {
                "malformed-answer" => page with { Messages = [answer, excluded with { AssistantResponse = answer.AssistantResponse }] },
                "malformed-user" => page with { Messages = [answer, excluded with { Body = "\ud800" }] },
                "snapshot" => page with { HeadRevision = page.HeadRevision + 1 },
                _ => page
            };
        };
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(value => value.SearchConversationAsync(f.Session.SessionId, "absent", null, 25,
            cancellation.Token, ConversationSearchRole.ASSISTANT)).Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("private")]
    [InlineData("source")]
    [InlineData("control")]
    [InlineData("snapshot")]
    public async Task AssistantCommandSourceRevocationDiscardsLateAnswers(string fault)
    {
        using var f = new ConversationFixture();
        f.Messages = [SearchAnswer(f, 1)];
        var admitted = true;
        f.AfterRead = page =>
        {
            if (fault is "private") { f.CanInspect = false; }
            if (fault is "source") { admitted = false; }
            if (fault is "control") { f.AdvanceRevision(); }
            if (fault is "snapshot") { f.Session = f.Session with { Generation = new(2) }; }
            return page;
        };
        var service = SearchCommands(f);
        await service.Invoking(value => value.ExecuteAsync(SearchCommand(f, "café") with
        { ConversationRole = ConversationSearchRole.ASSISTANT }, RequestOrigin.LocalUi, () => admitted, f.Token))
            .Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
    }
}
