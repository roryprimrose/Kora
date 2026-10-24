using AwesomeAssertions;
using Kora.Application.Memory;
using Kora.Core.Memory;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("inactive")]
    [InlineData("private")]
    [InlineData("cancel-read")]
    [InlineData("source-read")]
    [InlineData("retire-timer")]
    [InlineData("fail-timer")]
    public async Task MemoryReviewFailsClosedAcrossReadAndTimerAdmissionBoundaries(string fault)
    {
        using var f = new Fixture();
        var source = new ContextMemories(f);
        var time = new MemoryReviewTime();
        using var workflow = new SessionMemoryContext(source, f, time);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var target = new SessionWorkspaceEntry(f.Session, null);
        switch (fault)
        {
            case "inactive": target = target with { Authority = f.Session with { IsActive = false } }; break;
            case "private": f.CanControl = false; break;
            case "cancel-read": source.OnRead = cancellation.Cancel; break;
            case "source-read": source.OnRead = source.Invalidate; break;
            case "retire-timer": time.OnCreateTimer = workflow.Retire; break;
            case "fail-timer": time.FailTimer = true; break;
        }
        await workflow.Invoking(value => value.ReviewAsync(target, source.Selection, "question",
            static () => true, cancellation.Token)).Should().ThrowAsync<Exception>();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SynchronousExpiryRetiresCreatedReviewAndTimerWithoutPublishingPrivateBodies()
    {
        using var f = new Fixture();
        var source = new ContextMemories(f);
        var time = new MemoryReviewTime { ExpireDuringCreation = true };
        using var workflow = new SessionMemoryContext(source, f, time);
        var review = await workflow.ReviewAsync(new(f.Session, null), source.Selection, "question", static () => true, Token);
        review.Records.Should().BeEmpty();
        review.UserRequest.Should().BeEmpty();
        time.TimerDisposed.Should().BeTrue();
        review.TryConsume().Should().BeFalse();
        review.Dispose();
    }

    [Fact]
    public async Task ExplicitReviewRetirementNotifiesOnceAndInvalidatesAllPrivateAccess()
    {
        using var f = new Fixture();
        var source = new ContextMemories(f);
        using var workflow = new SessionMemoryContext(source, f, new MemoryReviewTime());
        var review = await workflow.ReviewAsync(new(f.Session, null), source.Selection, "question", static () => true, Token);
        var notifications = 0;
        review.Retired += () => notifications++;
        review.Dispose();
        review.Dispose();
        review.TryConsume().Should().BeFalse();
        review.Invoking(value => value.ReadCurrent()).Should().Throw<InvalidOperationException>();
        notifications.Should().Be(1);
    }

    [Theory]
    [InlineData("consumer")]
    [InlineData("review")]
    [InlineData("changed-after-read")]
    public async Task MemoryUseRefusesRevokedConsumerOrReviewBeforeOrAfterAuthoritativeSourceRead(string fault)
    {
        using var f = new Fixture();
        var source = new ContextMemories(f);
        using var workflow = new SessionMemoryContext(source, f, new MemoryReviewTime());
        var review = await workflow.ReviewAsync(new(f.Session, null), source.Selection, "question", static () => true, Token);
        var consumer = true;
        using var use = workflow.Confirm(review, () => consumer, Token);
        if (fault is "consumer") { consumer = false; }
        else if (fault is "review") { f.ControlRevision++; }
        else { source.OnRead = () => consumer = false; }
        await use.Invoking(value => value.ValidateAsync(Token)).Should().ThrowAsync<Exception>();
        review.Records.Should().BeEmpty();
        review.UserRequest.Should().BeEmpty();
    }
}
