using AwesomeAssertions;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Fact]
    public async Task MoreThan64DraftBearingSessionsHaveNoAggregateQuotaOrEvictionAndClearPreservesRevision()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        ConversationDraftSnapshot? first = null;
        for (var index = 0; index < 70; index++)
        {
            if (index != 0)
            {
                f.Request = InteractionStorageFixture.NewRequest();
                await f.AdmitAsync(newSession: true);
                await FinishInitial(f);
            }
            var saved = await Save(f, await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "draft-" + index);
            first ??= saved;
        }
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await FinishInitial(f);
        var additional = await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token);
        await Save(f, additional, "another explicitly saved draft");
        (await f.Store.ReadConversationDraftAsync(first!.Session.SessionId, f.Token)).Draft.Body.Should().Be("draft-0");
        await Save(f, first, "exact revision overwrite");
        var current = await f.Store.ReadConversationDraftAsync(first.Session.SessionId, f.Token);
        await Save(f, current, null);
        var additionalCurrent = await f.Store.ReadConversationDraftAsync(additional.Session.SessionId, f.Token);
        await Save(f, additionalCurrent, "new explicit draft");
        f.Count("conversation_drafts").Should().Be(71, "there is no aggregate draft-bearing session quota and clearing retains its strict revision");
    }

    [Fact]
    public async Task PerSession100MessageLimitDoesNotLimitOtherSmallSessionsAndPagesRemainBounded()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var first = f.Request.SessionId;
        for (var item = 0; item < 100; item++)
        {
            var receipt = await Submit(f, first, "original-" + item);
            receipt.Message.Sequence.Should().Be(item + 1);
        }
        await this.Invoking(_ => Submit(f, first, "session overflow"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*100 per session*");
        for (var session = 0; session < 70; session++)
        {
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await FinishInitial(f);
            await Submit(f, f.Request.SessionId, "independent small session " + session);
        }
        var page = await f.Store.ReadConversationPageAsync(first, null, 50, f.Token);
        page.Messages[0].Body.Should().Be("original-0");
        page.Messages.Should().HaveCount(50);
        var tail = await f.Store.ReadConversationPageAsync(first, page.Next, 50, f.Token);
        tail.Messages[^1].Body.Should().Be("original-99");
        f.Count("conversation_messages").Should().Be(170);
    }
}
