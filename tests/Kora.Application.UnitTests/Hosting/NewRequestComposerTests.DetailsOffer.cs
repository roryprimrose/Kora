using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class NewRequestComposerTests
{
    [Fact]
    public async Task OptInWithoutAForegroundProducerCannotCreateQuestionAuthority()
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var service = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, new RequestTime());
        var target = new ForegroundConversationTarget(f.Entry!, static () => true);
        var review = await service.ExecuteAsync(SessionCommand.Parse("ask this conversation with details offer exact input", "Kora")!,
            RequestOrigin.LocalUi, static () => true, TestContext.Current.CancellationToken, target);
        review.RequestReview!.OfferDetailsOnce.Should().BeTrue();
        service.SetDetailsOfferChoice(true).OfferDetailsOnce.Should().BeTrue();
        var completed = await service.ExecuteAsync(Confirm(review.RequestReview), RequestOrigin.LocalUi,
            static () => true, TestContext.Current.CancellationToken);
        service.TakeDetailsOffer(completed.RequestSubmission!).Should().BeNull();
        var addressed = new SessionCommand(SessionCommandOperation.RequestReview,
            f.Entry!.Authority.SessionId.Value, f.Entry.Authority.Generation.Value, f.Entry.Metadata?.Revision.Value ?? 0)
        { RequestDetailsOffer = true, RequestBody = "not foreground" };
        await service.Invoking(value => value.ExecuteAsync(addressed, RequestOrigin.LocalUi, static () => true,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        f.Retentions.Should().Be(1);
    }
}