using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class NewRequestComposerTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ForegroundReviewUsesExactHostSnapshotAndSingleUseLocalSubmission(RequestOrigin origin)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var service = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, new RequestTime());
        var target = new ForegroundConversationTarget(f.Entry!, () => true);
        const string body = "  shutdown e\u0301\r\n\tunchanged  ";
        var command = SessionCommand.Parse("ask this conversation " + body, "Nova")!;
        var review = (await service.ExecuteAsync(command, origin, () => true,
            TestContext.Current.CancellationToken, target)).RequestReview!;
        f.Retentions.Should().Be(0);
        review.Target.Should().Be(target.Entry);
        var result = await service.ExecuteAsync(new(SessionCommandOperation.RequestConfirm) { RequestUsesPendingReview = true },
            RequestOrigin.LocalUi, () => true, TestContext.Current.CancellationToken);
        result.RequestSubmission!.Message.Body.Should().Be(body);
        result.RequestSubmission.Message.Request.Origin.Should().Be(origin);
        f.ModelAdmissions.Should().Be(1);
        f.Retentions.Should().Be(1);
        await service.Invoking(value => value.ExecuteAsync(Confirm(review), origin, () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("retired")]
    [InlineData("ineligible")]
    [InlineData("operation")]
    [InlineData("id")]
    [InlineData("active")]
    [InlineData("name")]
    [InlineData("provider")]
    [InlineData("metadata")]
    public async Task ForegroundCommandNeverUsesCachedIdsOrInventoryFallback(string fault)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var service = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, new RequestTime());
        var target = new ForegroundConversationTarget(f.Entry!, () => fault is not "ineligible");
        var command = SessionCommand.Parse("ask this conversation original", "Nova")!;
        if (fault is "retired") { target.Retire(); }
        if (fault is "operation") { command = command with { Operation = SessionCommandOperation.RequestConfirm }; }
        if (fault is "id") { command = command with { SessionId = f.Entry!.Authority.SessionId.Value }; }
        if (fault is "active") { command = command with { RequestActiveTarget = true }; }
        if (fault is "name") { command = command with { RequestTargetName = new("topic") }; }
        if (fault is "provider") { command = command with { RequestProvider = SessionRequestReview.ProviderChoice.Copilot }; }
        if (fault is "metadata") { f.Entry = f.Entry! with { Metadata = f.Entry!.Metadata! with { Name = new("renamed") } }; }
        await service.Invoking(value => value.ExecuteAsync(command, RequestOrigin.LocalUi, () => true,
            TestContext.Current.CancellationToken, fault is "missing" ? null : target)).Should().ThrowAsync<InvalidOperationException>();
        f.Retentions.Should().Be(0);
        f.ModelAdmissions.Should().Be(0);
    }

    [Theory]
    [InlineData("retired")]
    [InlineData("source")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    public async Task ForegroundReviewCannotReviveAfterRetirementExpiryOrCancel(string loss)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var time = new RequestTime();
        var service = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, time);
        var current = true;
        var target = new ForegroundConversationTarget(f.Entry!, () => current);
        var review = (await service.ExecuteAsync(SessionCommand.Parse("ask this conversation original", "Nova")!,
            RequestOrigin.ActivatedVoice, () => true, TestContext.Current.CancellationToken, target)).RequestReview!;
        if (loss is "retired") { target.Retire(); }
        if (loss is "source") { current = false; }
        if (loss is "expiry") { time.Now = review.ExpiresAt; }
        if (loss is "cancel")
        {
            (await service.ExecuteAsync(new(SessionCommandOperation.RequestCancel) { RequestUsesPendingReview = true },
                RequestOrigin.LocalUi, () => true, TestContext.Current.CancellationToken)).Outcome.Should().Be("cancelled");
        }
        await service.Invoking(value => value.ExecuteAsync(Confirm(review), RequestOrigin.LocalUi, () => true,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        current = true;
        target.IsCurrent.Should().Be(loss is not "retired");
        f.Retentions.Should().Be(0);
        f.ModelAdmissions.Should().Be(0);
    }
}
