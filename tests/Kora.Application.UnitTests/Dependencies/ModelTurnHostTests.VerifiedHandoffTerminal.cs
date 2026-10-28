using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public async Task VerifiedSuccessorRetainsOnlyTheGenuineTypedTerminalWithoutInventingAnAnswer(ModelTurnOutcome outcome)
    {
        using var f = new Fixture();
        var (host, workflow, operation, runtime, presented) = f.VerifiedOperation();
        runtime.Reply = new(outcome);
        f.HandoffReviews.Offered += exact =>
        {
            using var presentation = exact.Presentation();
            f.HandoffReviews.Confirm(exact, true, RequestOrigin.LocalUi).Should().BeTrue();
        };
        var running = operation.RunAsync(new(f.Session, null), "original", static () => true, Token);
        await presented.Task.WaitAsync(Token);
        var source = new ModelHandoffPresentation(workflow);
        var offer = (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Single();
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(Token);
        f.Time.ExpectedDeadline = HostedTurnAuthorization.MaximumDuration;
        await review.Decide(offer, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token);
        var result = await running;
        result.Message.Outcome.Should().Be(outcome);
        result.Task.State.Should().Be(HostTaskState.Unknown);
        result.Message.AssistantResponse.Should().BeNull();
        runtime.Transmissions.Should().Be(1);
        f.HandoffCommits.Should().Be(1);
        host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task LocalUnavailableCannotOfferAnUnconfiguredHostedTuple()
    {
        using var f = new Fixture();
        var (_, workflow, operation, runtime, presented) = f.VerifiedOperation();
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var result = await operation.RunAsync(new(f.Session, null), "original", static () => true, Token);
        result.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        workflow.Pending().Should().BeEmpty();
        presented.Task.IsCompleted.Should().BeFalse();
        runtime.Calls.Should().Be(0);
        f.HandoffCommits.Should().Be(0);
    }

    [Fact]
    public async Task QualificationLossAfterQuestionCommitRetiresTheUnpublishedQuestionBeforeSourceCompletion()
    {
        using var f = new Fixture();
        var (_, workflow, operation, runtime, presented) = f.VerifiedOperation();
        f.OnAudit = audit =>
        {
            if (string.Equals(audit.ActionId, "model.handoff", StringComparison.Ordinal)
                && audit.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded)
            { f.ProfileCatalogue.Status = ProviderCatalogueStatus.AuthenticationRequired; }
        };
        var result = await operation.RunAsync(new(f.Session, null), "original", static () => true, Token);
        result.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        f.Questions.Snapshot.Questions.Should().ContainSingle().Which.Status.Should().Be(QuestionStatus.Cancelled);
        workflow.Pending().Should().BeEmpty();
        presented.Task.IsCompleted.Should().BeFalse();
        f.HandoffCommits.Should().Be(0);
        runtime.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AuthoritativeUnavailableBeforePhysicalDispatchProvesOnlyItsOwnZeroResourceJoin()
    {
        using var f = new Fixture();
        var (host, workflow, operation, runtime, presented) = f.VerifiedOperation();
        f.AfterExecutionStart = () => f.ProfileCatalogue.Installation = LocalProviderInstallation.Missing;
        var running = operation.RunAsync(new(f.Session, null), "original", static () => true, Token);
        await presented.Task.WaitAsync(Token);
        var source = new ModelHandoffPresentation(workflow);
        var offer = (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Single();
        offer.LocalUnavailable!.Result.Reason.Should().Be(ModelTurnReason.UnregisteredModel);
        offer.LocalUnavailable.IsCurrent.Should().BeTrue();
        f.Adapter.Calls.Should().Be(0);
        f.Audits.Should().Contain(item => string.Equals(item.ActionId, "runtime.resource.release", StringComparison.Ordinal)
            && item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        await review.Refresh(Token);
        f.HandoffReviews.Offered += exact =>
        {
            using var presentation = exact.Presentation();
            f.HandoffReviews.Confirm(exact, true, RequestOrigin.LocalUi).Should().BeTrue();
        };
        f.Time.ExpectedDeadline = HostedTurnAuthorization.MaximumDuration;
        await review.Decide(offer, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token);
        (await running).Message.AssistantResponse!.Body.Should().Be("actual-boundary-answer");
        runtime.Transmissions.Should().Be(1);
        f.Adapter.Calls.Should().Be(0);
        host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task QuestionRetirementDenialIsExplicitAndCannotClaimAnAtomicHandoff()
    {
        using var f = new Fixture();
        var (_, workflow, operation, runtime, presented) = f.VerifiedOperation();
        var running = operation.RunAsync(new(f.Session, null), "original", static () => true, Token);
        await presented.Task.WaitAsync(Token);
        var source = new ModelHandoffPresentation(workflow);
        var offer = (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Single();
        await using var review = source.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        f.Questions.RefusedDecision = new(HostInteractionOutcome.Denied, "synthetic-store-denial");
        review.Revoke();
        var failure = await Assert.ThrowsAsync<ConversationSubmissionUnconfirmedException>(async () => await running);
        failure.InnerException.Should().BeOfType<InvalidOperationException>();
        f.Questions.Snapshot.Questions.Single().Status.Should().Be(QuestionStatus.Pending);
        runtime.Transmissions.Should().Be(0);
        f.HandoffCommits.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SuccessorWithoutAnAdmittedTurnCannotClaimSuccessOrAResponse(bool success)
    {
        using var f = new Fixture();
        var (host, _, _, _, _) = f.VerifiedOperation();
        var request = f.Request;
        var context = f.Context();
        var retained = new ConversationSubmissionReceipt(new(request, f.Session.Generation, 1, 1, f.Time.Now,
            context.UserRequest, null, null), f.Task!.Task);
        var admission = new ModelTurnAdmission(new(success ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Denied,
            ModelTurnReason.ProviderOutcome, Response: success ? null : new("counterfeit answer", null)));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.VerifiedConversation.ExecuteHandoffSuccessorAsync(
            new(f.Session, null), retained, admission, context, static () => true, Token));
        host.IsQuiescent.Should().BeTrue();
        f.RetainCalls.Should().Be(0);
    }
}
