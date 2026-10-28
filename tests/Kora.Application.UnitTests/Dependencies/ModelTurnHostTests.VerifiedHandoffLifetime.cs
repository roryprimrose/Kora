using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("close")]
    [InlineData("expiry")]
    [InlineData("expired-before-wait")]
    [InlineData("revoked-observation")]
    [InlineData("changed-context")]
    [InlineData("changed-account-after-commit")]
    [InlineData("expired-during-account-observation")]
    [InlineData("commit-failure")]
    [InlineData("metadata-after-source")]
    [InlineData("message-after-source")]
    public async Task VerifiedHandoffLifetimeNeverRenewsAuthorityOrPublishesAnUnconfirmedSuccess(string change)
    {
        using var f = new Fixture();
        var (host, workflow, operation, runtime, presented) = f.VerifiedOperation();
        if (change is "expired-before-wait") { operation.ReviewRequested += () => f.Time.Now = f.Time.Now.AddMinutes(1); }
        if (change is "commit-failure") { f.BeforeHandoffCommit = () => throw new IOException("injected canonical failure"); }
        if (change is "changed-account-after-commit") { f.AfterHandoffCommit = () => f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]); }
        if (change is "expired-during-account-observation")
        {
            f.AfterHandoffCommit = () => f.ProfileCatalogue.AfterObservation = () =>
            {
                f.Time.Now = f.LastHandoffCommit!.Successor.Message.Handoff!.ExpiresAt;
                f.ProfileCatalogue.AfterObservation = null;
            };
        }
        if (change is "metadata-after-source" or "message-after-source")
        {
            f.VerifiedConversation.RunningConversationChanged += status =>
            {
                if (status.Phase != ConversationExecutionPhase.Terminal) { return; }
                if (change is "metadata-after-source") { f.Session = f.Session with { Generation = new(2) }; }
                else { f.Retained = f.Retained! with { Body = "changed exact successor" }; }
            };
        }
        var running = operation.RunAsync(new(f.Session, null), "original", static () => true, Token);
        await presented.Task.WaitAsync(Token);
        if (change is "expired-before-wait")
        {
            (await running).Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
            f.HandoffCommits.Should().Be(0);
            return;
        }
        var presentation = new ModelHandoffPresentation(workflow);
        var offer = (await presentation.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Single();
        var review = presentation.Open(offer, RequestOrigin.LocalUi, static () => true)!;
        if (change is "close")
        {
            presentation.RevokeAll();
            workflow.Pending().Should().BeEmpty();
        }
        else if (change is "expiry")
        {
            f.Time.Now = offer.Context.ExpiresAt;
            f.Time.FireDeadline();
        }
        else if (change is "revoked-observation" or "changed-context")
        {
            using var activity = workflow.Continuation(offer)!();
            if (change is "revoked-observation")
            {
                f.CanControl = false;
                (await host.ObserveHandoffAsync(offer.Policy, offer.Context, Token, offer.LocalUnavailable)).Reason
                    .Should().Be(ModelTurnReason.HandoffReviewStale);
                f.CanControl = true;
                offer.LocalUnavailable!.IsCurrent.Should().BeFalse("withdrawn authority must not revive");
            }
            else
            {
                var invalid = new ModelContextEnvelope(offer.Context.Request, "changed", offer.Context.UserRequest, [],
                    offer.Context.CreatedAt, offer.Context.ExpiresAt, offer.Context.ToolMode);
                (await host.ObserveHandoffAsync(offer.Policy, invalid, Token, offer.LocalUnavailable)).Reason
                    .Should().Be(ModelTurnReason.HandoffReviewStale);
            }
            review.Revoke();
        }
        else
        {
            await review.Refresh(Token);
            var failure = await Record.ExceptionAsync(() => review.Decide(offer, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token));
            if (change is "commit-failure" or "metadata-after-source" or "message-after-source") { failure.Should().NotBeNull(); }
            else { failure.Should().BeNull(); }
        }
        var operationFailure = await Record.ExceptionAsync(async () => await running);
        var disposalFailure = await Record.ExceptionAsync(async () => await review.DisposeAsync());
        if (change is "commit-failure" or "metadata-after-source" or "message-after-source")
        {
            operationFailure.Should().NotBeNull();
            disposalFailure.Should().NotBeNull();
        }
        else
        {
            operationFailure.Should().BeNull();
            disposalFailure.Should().BeNull();
        }
        runtime.Transmissions.Should().Be(0);
        f.Retained!.AssistantResponse.Should().BeNull();
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("closed")]
    [InlineData("catalogue")]
    [InlineData("profile")]
    [InlineData("selection")]
    [InlineData("generation")]
    [InlineData("revision")]
    [InlineData("policy")]
    public async Task VerifiedTargetReobservationRejectsForeignAccountSelectionAndSessionPolicy(string change)
    {
        using var f = new Fixture();
        var (host, workflow, operation, _, presented) = f.VerifiedOperation();
        var running = operation.RunAsync(new(f.Session, null), "original", static () => true, Token);
        await presented.Task.WaitAsync(Token);
        var source = new ModelHandoffPresentation(workflow);
        var original = (await source.ReadPending(RequestOrigin.LocalUi, static () => true, Token)).Single();
        var target = original.Target!;
        var changed = change switch
        {
            "profile" => target with { Profile = target.Profile with { Account = new("foreign") } },
            "selection" => target with { Selection = target.Selection with { Revision = new(2) } },
            "generation" => target with { Session = target.Session with { Session = target.Session.Session with { Generation = new(2) } } },
            "revision" => target with { Session = target.Session with { Revision = target.Session.Revision + 1 } },
            "policy" => target with { Session = target.Session with { Desired = new(ProviderService.Local) } },
            _ => target,
        };
        var offer = new ModelHandoffOffer(host, original.Policy, original.Context, original.Reason, original.Question,
            original.Generation, original.TaskRevision, original.ControlRevision)
        {
            LocalUnavailable = original.LocalUnavailable,
            Target = change is "missing" ? null : changed,
        };
        if (change is "closed") { f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]); }
        if (change is "catalogue") { f.ProfileCatalogue.Status = ProviderCatalogueStatus.AuthenticationRequired; }
        using (var activity = workflow.Continuation(original)!())
        {
            (await host.HandoffTargetCurrentAsync(offer, Token)).Should().BeFalse();
            if (change is not "missing")
            {
                (await host.CheckHandoffAsync(offer, Token)).Reason.Should().Be(ModelTurnReason.HandoffReviewStale);
            }
        }
        await using var review = source.Open(original, RequestOrigin.LocalUi, static () => true)!;
        review.Revoke();
        (await running).Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        f.HandoffCommits.Should().Be(0);
    }
}
