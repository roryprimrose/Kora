using AwesomeAssertions;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
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
