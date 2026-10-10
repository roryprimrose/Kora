using AwesomeAssertions;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Network;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class HostAuthorizationServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WebRequestsFailClosedWithoutAnExactProposalOrPreapprovalPolicy(bool missingProposal)
    {
        var address = new Uri("https://example.com/page");
        using var f = new InteractionFixture();
        f.ChangeProposal(binding: WebBinding(f.Store.Snapshot.Proposal!.Binding, address));
        if (missingProposal) { f.Store.Change(snapshot => snapshot with { Proposal = null }); }
        var decision = await f.RunAsync(() =>
            f.Authorization.RequestWebPageAccessAsync(f.Request, address, CancellationToken.None));
        decision.Reason.Should().Be(missingProposal
            ? "web-destination-not-admitted" : "preapproved-uri-policy-unavailable");
        decision.Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Snapshot.Questions.Should().BeEmpty();
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task Preapproved_web_destination_satisfies_only_the_address_grant()
    {
        var address = new Uri("https://api.example.com/page");
        using var f = new InteractionFixture(
            preapprovedUris: new UriConfiguration(["https://*.example.com/*"]));
        f.ChangeProposal(binding: WebBinding(f.Store.Snapshot.Proposal!.Binding, address));

        var decision = await f.RunAsync(() =>
            f.Authorization.RequestWebPageAccessAsync(f.Request, address, CancellationToken.None));

        decision.Outcome.Should().Be(HostInteractionOutcome.Approved);
        decision.Reason.Should().Be("preapproved-address");
        f.Store.Snapshot.Questions.Should().BeEmpty();
        f.Store.Snapshot.Grants.Should().BeEmpty();
        f.Store.Change(snapshot => snapshot with
        {
            Policy = snapshot.Policy with { OtherMandatoryGatesSatisfied = false },
        });
        (await f.RunAsync(() =>
            f.Authorization.RequestWebPageAccessAsync(f.Request, address, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
    }

    [Fact]
    public async Task Unmatched_web_destination_uses_the_normal_exact_grant_question()
    {
        var address = new Uri("https://other.example/page");
        using var f = new InteractionFixture(
            preapprovedUris: new UriConfiguration(["https://approved.example/*"]));
        f.ChangeProposal(binding: WebBinding(f.Store.Snapshot.Proposal!.Binding, address));

        var presented = await f.RunAsync(() =>
            f.Authorization.RequestWebPageAccessAsync(f.Request, address, CancellationToken.None));

        presented.Outcome.Should().Be(HostInteractionOutcome.Presented);
        presented.Question!.Proposal.Should().Be(f.Store.Snapshot.Proposal);
        var grant = await f.RunAsync(() => f.Authorization.ApproveAsync(
            presented.Question.Key,
            new(["once"]),
            RequestOrigin.LocalUi,
            CancellationToken.None));
        grant.Outcome.Should().Be(HostInteractionOutcome.Approved);
    }

    [Fact]
    public async Task Protected_call_policy_ignores_preapproval_and_requires_fresh_single_use_review()
    {
        var address = new Uri("https://approved.example/page");
        using var f = new InteractionFixture(
            RequestOrigin.ActivatedVoice,
            new UriConfiguration(["https://approved.example/*"]));
        f.ChangeProposal(binding: WebBinding(f.Store.Snapshot.Proposal!.Binding, address));
        f.Store.Change(snapshot => snapshot with
        {
            Policy = snapshot.Policy with
            {
                IsProtectedCall = true,
                IgnoreReusableGrants = true,
            },
        });

        var presented = await f.RunAsync(() =>
            f.Authorization.RequestWebPageAccessAsync(f.Request, address, CancellationToken.None));

        presented.Outcome.Should().Be(HostInteractionOutcome.Presented);
        var approved = await f.RunAsync(() => f.Authorization.ApproveAsync(
            presented.Question!.Key,
            new(["once"]),
            RequestOrigin.LocalUi,
            CancellationToken.None));
        approved.Outcome.Should().Be(HostInteractionOutcome.Approved);
        approved.Grant!.Scope.Should().Be(OperationGrantScope.Once);
    }

    [Fact]
    public async Task Redirect_or_hostile_raw_address_requires_a_fresh_destination_bound_proposal()
    {
        var original = new Uri("https://approved.example/start");
        var redirect = new Uri("https://redirect.example/final");
        using var f = new InteractionFixture(
            preapprovedUris: new UriConfiguration(["https://approved.example/*"]));
        var prior = f.Store.Snapshot.Proposal!.Binding;
        f.ChangeProposal(binding: WebBinding(prior, original));
        (await f.RunAsync(() =>
            f.Authorization.RequestWebPageAccessAsync(f.Request, original, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Approved);

        var denied = await f.RunAsync(() =>
            f.Authorization.RequestWebPageAccessAsync(f.Request, redirect, CancellationToken.None));
        denied.Outcome.Should().Be(HostInteractionOutcome.Denied);
        denied.Reason.Should().Be("web-destination-not-admitted");

        f.ChangeProposal(binding: WebBinding(prior, redirect), revision: new(2));
        (await f.RunAsync(() =>
            f.Authorization.RequestWebPageAccessAsync(f.Request, redirect, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Presented);
    }

    [Fact]
    public async Task Corrupt_preapproval_state_fails_without_creating_authority()
    {
        var address = new Uri("https://example.com/page");
        using var f = new InteractionFixture(preapprovedUris: new UriConfiguration([], corrupt: true));
        f.ChangeProposal(binding: WebBinding(f.Store.Snapshot.Proposal!.Binding, address));

        var request = async () => await f.RunAsync(() =>
            f.Authorization.RequestWebPageAccessAsync(f.Request, address, CancellationToken.None));

        await request.Should().ThrowAsync<InvalidDataException>();
        f.Store.Snapshot.Questions.Should().BeEmpty();
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData("once", OperationGrantScope.Once)]
    [InlineData("session", OperationGrantScope.Session)]
    [InlineData("perpetual", OperationGrantScope.Perpetual)]
    public async Task Approval_binds_exact_host_operation_and_does_not_dispatch(string scope, OperationGrantScope expected)
    {
        using var f = new InteractionFixture(RequestOrigin.ActivatedVoice);
        var grant = await f.GrantAsync(scope);
        grant.Scope.Should().Be(expected);
        grant.ApprovedProposal.Should().Be(f.Store.Snapshot.Proposal);
        grant.ApprovedProposal.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        grant.CreatorChannel.Should().Be(RequestOrigin.LocalUi);
        grant.CreatedAt.Should().Be(f.Time.Now);
        grant.UseCount.Should().Be(0);
        grant.LastUsedAt.Should().BeNull();
        grant.SessionGeneration.Value.Should().Be(1);
        grant.Status.Should().Be(OperationGrantStatus.Active);
        grant.RevocationReason.Should().BeNull();
        f.Store.Snapshot.Intent.State.Should().Be(HostTaskState.IntentRecorded);
        var consumed = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None));
        consumed.Outcome.Should().Be(HostInteractionOutcome.Consumed);
        consumed.Grant!.UseCount.Should().Be(1);
        consumed.Grant.LastUsedAt.Should().Be(f.Time.Now);
        consumed.Grant.Status.Should().Be(expected == OperationGrantScope.Once ? OperationGrantStatus.Consumed : OperationGrantStatus.Active);
    }

    [Fact]
    public async Task Concurrent_duplicate_approval_and_single_use_consumption_commit_once()
    {
        using var f = new InteractionFixture();
        var presented = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        var approvals = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(async () =>
            await f.RunAsync(() => f.Authorization.ApproveAsync(presented.Question!.Key, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None)))));
        approvals.Count(r => r.Outcome == HostInteractionOutcome.Approved).Should().Be(1);
        approvals.Count(r => r.Outcome == HostInteractionOutcome.Conflict).Should().Be(23);
        var grant = f.Store.Snapshot.Grants.Single();
        var uses = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(async () =>
            await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None)))));
        uses.Count(r => r.Outcome == HostInteractionOutcome.Consumed).Should().Be(1);
        uses.Count(r => r.Outcome == HostInteractionOutcome.Conflict).Should().Be(23);
        f.Store.Snapshot.Grants.Single().UseCount.Should().Be(1);
        f.Store.Commits.Count(c => c.Decision.Outcome == HostInteractionOutcome.Consumed).Should().Be(1);
    }

    [Fact]
    public async Task Expired_cancelled_closed_or_wrong_revision_approval_is_not_authority()
    {
        using var f = new InteractionFixture();
        var presented = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        var key = presented.Question!.Key;
        var wrong = new HostQuestionKey(f.Request, key.QuestionId, new(2));
        (await f.RunAsync(() => f.Authorization.ApproveAsync(wrong, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        var duplicate = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        duplicate.Reason.Should().Be("duplicate-proposal");
        f.Time.Now = presented.Question.ExpiresAt;
        var expired = await f.RunAsync(() => f.Authorization.ApproveAsync(key, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None));
        expired.Outcome.Should().Be(HostInteractionOutcome.Expired);
        (await f.RunAsync(() => f.Authorization.ApproveAsync(expired.Question!.Key, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        f.Time.Now = f.Time.Now.AddMinutes(-1);
        f.ChangeProposal(revision: new(2));
        var next = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        var cancelled = await f.RunAsync(() => f.Questions.CancelAsync(next.Question!.Key, CancellationToken.None));
        (await f.RunAsync(() => f.Authorization.ApproveAsync(cancelled.Question!.Key, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HostOperationEffect.Unknown)]
    [InlineData(HostOperationEffect.Prohibited)]
    public async Task Unknown_or_prohibited_effects_remain_denied_even_with_a_matching_grant(HostOperationEffect effect)
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync("perpetual");
        f.ChangeProposal(effect: effect);
        (await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Snapshot.Grants[0].UseCount.Should().Be(0);
    }

    [Fact]
    public async Task Protected_call_voice_origin_cannot_be_laundered_by_ui_confirmation_or_grant()
    {
        using var f = new InteractionFixture(RequestOrigin.ActivatedVoice);
        f.ChangeProposal(effect: HostOperationEffect.VoiceOrCallSettings);
        var presented = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        f.Store.Change(s => s with { Policy = s.Policy with { IsProtectedCall = true } });
        var uiReply = await f.RunAsync(() => f.Authorization.ApproveAsync(presented.Question!.Key, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None));
        uiReply.Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Change(s => s with { Policy = s.Policy with { IsProtectedCall = false } });
        var grant = await f.GrantAfterPresentationAsync(presented.Question!);
        f.Store.Change(s => s with { Policy = s.Policy with { IsProtectedCall = true } });
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Rebind(origin: RequestOrigin.LocalUi);
        var fresh = await f.GrantAsync();
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, fresh.Id, fresh.Revision, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Consumed);
    }

    [Theory]
    [InlineData("once", HostInteractionOutcome.Consumed)]
    [InlineData("session", HostInteractionOutcome.Denied)]
    [InlineData("perpetual", HostInteractionOutcome.Denied)]
    public async Task In_call_default_ignore_requires_single_use_without_deleting_reusable_records(string scope, HostInteractionOutcome outcome)
    {
        using var f = new InteractionFixture(RequestOrigin.ActivatedVoice);
        var grant = await f.GrantAsync(scope, RequestOrigin.ActivatedVoice);
        f.Store.Change(s => s with { Policy = s.Policy with { IsProtectedCall = true } });
        var result = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None));
        result.Outcome.Should().Be(outcome);
        if (!string.Equals(scope, "once", StringComparison.Ordinal))
        {
            f.Store.Snapshot.Grants[0].Should().Be(grant);
            f.Store.Change(s => s with { Policy = s.Policy with { IgnoreReusableGrants = false } });
            (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None)))
                .Outcome.Should().Be(HostInteractionOutcome.Consumed);
        }
    }

    [Theory]
    [InlineData("once")]
    [InlineData("session")]
    public async Task Done_delete_and_resume_generation_end_nonperpetual_authority(string scope)
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync(scope);
        f.Store.Change(s => s with { Session = s.Session with { IsActive = false } });
        var ended = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None));
        ended.Outcome.Should().Be(HostInteractionOutcome.Revoked);
        ended.Grant!.RevocationReason.Should().Be("work-session-ended");
        f.Store.Change(s => s with { Session = s.Session with { IsActive = true, Generation = new(2) } });
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, ended.Grant.Revision, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
    }

    [Fact]
    public async Task Resume_before_use_does_not_restore_session_grants_or_pending_approvals()
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync("session");
        f.ChangeProposal(revision: new(2));
        var pending = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        f.Store.Change(s => s with { Session = s.Session with { Generation = new(2) } });
        (await f.RunAsync(() => f.Authorization.ApproveAsync(pending.Question!.Key, new(["session"]), RequestOrigin.LocalUi, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Revoked);
    }

    [Theory]
    [InlineData("once", false)]
    [InlineData("session", false)]
    [InlineData("perpetual", true)]
    public async Task Cross_session_scope_is_explicit_and_perpetual_survives_history_removal(string scope, bool allowed)
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync(scope);
        f.Rebind(newSession: true);
        f.Store.Change(s => s with { Questions = [] });
        f.Time.Now = f.Time.Now.AddYears(100);
        f.Rebind();
        var result = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None));
        result.Outcome.Should().Be(allowed ? HostInteractionOutcome.Consumed : HostInteractionOutcome.Denied);
        f.Store.Snapshot.Grants.Should().ContainSingle();
        if (allowed)
        {
            var restartedService = new Kora.Application.Interaction.HostAuthorizationService(f.Store, f.Time);
            var reused = await f.RunAsync(() => restartedService.ConsumeAsync(f.Request, grant.Id, result.Grant!.Revision, CancellationToken.None));
            reused.Outcome.Should().Be(HostInteractionOutcome.Consumed);
            reused.Grant!.CreatedAt.Should().Be(grant.CreatedAt);
        }
    }

    [Theory]
    [InlineData(0, HostInteractionOutcome.Revoked)]
    [InlineData(1, HostInteractionOutcome.Revoked)]
    [InlineData(2, HostInteractionOutcome.Revoked)]
    [InlineData(3, HostInteractionOutcome.Revoked)]
    [InlineData(4, HostInteractionOutcome.Denied)]
    [InlineData(5, HostInteractionOutcome.Denied)]
    [InlineData(6, HostInteractionOutcome.Denied)]
    [InlineData(7, HostInteractionOutcome.Denied)]
    [InlineData(8, HostInteractionOutcome.Denied)]
    public async Task Every_exact_binding_dimension_is_revalidated(int field, HostInteractionOutcome expected)
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync("perpetual");
        f.ChangeProposal(binding: InteractionFixture.Binding(field));
        var result = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None));
        result.Outcome.Should().Be(expected);
        f.ChangeProposal(binding: InteractionFixture.Binding());
        var restored = f.Store.Snapshot.Grants[0];
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, restored.Revision, CancellationToken.None)))
            .Outcome.Should().Be(expected == HostInteractionOutcome.Revoked ? HostInteractionOutcome.Conflict : HostInteractionOutcome.Consumed);
    }

    [Fact]
    public async Task Proposal_revision_and_implementation_changes_invalidate_presented_review()
    {
        using var f = new InteractionFixture();
        var presented = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        f.ChangeProposal(revision: new(2));
        var stale = await f.RunAsync(() => f.Authorization.ApproveAsync(presented.Question!.Key, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None));
        stale.Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.ChangeProposal(binding: InteractionFixture.Binding(1));
        var changed = await f.RunAsync(() => f.Authorization.ApproveAsync(presented.Question!.Key, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None));
        changed.Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task Content_change_winning_approval_race_is_revalidated_inside_the_atomic_transition()
    {
        using var f = new InteractionFixture();
        var presented = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        f.Store.BeforeTransition = () => f.ChangeProposal(binding: InteractionFixture.Binding(1));
        var changed = await f.RunAsync(() => f.Authorization.ApproveAsync(presented.Question!.Key, new(["perpetual"]), RequestOrigin.LocalUi, CancellationToken.None));
        changed.Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Snapshot.Grants.Should().BeEmpty();
        f.Store.Snapshot.Questions[0].Status.Should().Be(QuestionStatus.Pending);
    }

    [Fact]
    public async Task Changed_known_effect_cannot_reuse_a_read_grant_for_a_write()
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync("perpetual");
        f.ChangeProposal(effect: HostOperationEffect.BoundedWrite);
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Snapshot.Grants[0].UseCount.Should().Be(0);
        f.ChangeProposal(revision: new(2));
        var fresh = await f.GrantAsync();
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, fresh.Id, fresh.Revision, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Consumed);
    }

    [Fact]
    public async Task Observed_changes_revoke_every_affected_grant_permanently_without_evicting_records()
    {
        using var f = new InteractionFixture();
        var first = await f.GrantAsync("perpetual");
        f.ChangeProposal(revision: new(2));
        var second = await f.GrantAsync("session");
        await f.RunAsync(() => f.Authorization.RevokeAsync(f.Request, second.Id, second.Revision, CancellationToken.None));
        f.ChangeProposal(binding: InteractionFixture.Binding(0));
        var observed = await f.RunAsync(() => f.Authorization.ObserveContentAsync(f.Request, CancellationToken.None));
        observed.Outcome.Should().Be(HostInteractionOutcome.Revoked);
        f.Store.Snapshot.Grants.Should().HaveCount(2).And.OnlyContain(g => g.Status == OperationGrantStatus.Revoked);
        f.ChangeProposal(binding: InteractionFixture.Binding());
        await f.RunAsync(() => f.Authorization.ObserveContentAsync(f.Request, CancellationToken.None));
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, first.Id, f.Store.Snapshot.Grants[0].Revision, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        f.Store.Change(s => s with { Proposal = null });
        (await f.RunAsync(() => f.Authorization.ObserveContentAsync(f.Request, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
    }

    [Fact]
    public async Task Explicit_remove_or_edit_is_revision_checked_and_never_widens_existing_authority()
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync("perpetual");
        (await f.RunAsync(() => f.Authorization.RevokeAsync(f.Request, new(Guid.NewGuid()), new(1), CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, new(Guid.NewGuid()), new(1), CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.RunAsync(() => f.Authorization.RevokeAsync(f.Request, grant.Id, new(2), CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        var revoked = await f.RunAsync(() => f.Authorization.RevokeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None));
        revoked.Grant!.RevocationReason.Should().Be("explicit-remove-or-edit");
        (await f.RunAsync(() => f.Authorization.RevokeAsync(f.Request, grant.Id, revoked.Grant.Revision, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, revoked.Grant.Revision, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        f.ChangeProposal(revision: new(2));
        var fresh = await f.GrantAsync("perpetual");
        fresh.Id.Should().NotBe(grant.Id);
        f.Store.Snapshot.Grants.Should().HaveCount(2);
    }

    [Fact]
    public async Task Revoke_before_consume_and_policy_change_before_commit_are_serialized()
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync("perpetual");
        f.Store.BeforeTransition = () => f.Store.Change(s => s with { Policy = s.Policy with { OtherMandatoryGatesSatisfied = false } });
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.BeforeTransition = null;
        f.Store.Change(s => s with { Policy = s.Policy with { OtherMandatoryGatesSatisfied = true } });
        var results = await Task.WhenAll(
            Task.Run(async () => await f.RunAsync(() => f.Authorization.RevokeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None))),
            Task.Run(async () => await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None))));
        results.Count(r => r.Outcome == HostInteractionOutcome.Conflict).Should().Be(1);
        var current = f.Store.Snapshot.Grants[0];
        if (current.Status == OperationGrantStatus.Active)
        {
            await f.RunAsync(() => f.Authorization.RevokeAsync(f.Request, current.Id, current.Revision, CancellationToken.None));
        }
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, f.Store.Snapshot.Grants[0].Revision, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
    }

    [Fact]
    public async Task Untrusted_answers_clarifications_and_missing_current_operation_do_not_authorize()
    {
        using var f = new InteractionFixture();
        var clarify = await f.QuestionAsync();
        (await f.RunAsync(() => f.Authorization.ApproveAsync(clarify.Key, new(["a"]), RequestOrigin.LocalUi, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        var presented = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        foreach (var channel in new[] { RequestOrigin.HostSystem, (RequestOrigin)99 })
        {
            (await f.RunAsync(() => f.Authorization.ApproveAsync(presented.Question!.Key, new(["once"]), channel, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        }
        (await f.RunAsync(() => f.Authorization.ApproveAsync(presented.Question!.Key, new(["model-says-always"]), RequestOrigin.LocalUi, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        var grant = await f.GrantAfterPresentationAsync(presented.Question!);
        f.Store.Change(s => s with { Proposal = null });
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
    }

    [Fact]
    public async Task Expired_dispatch_locked_session_missing_gate_and_wrong_operation_owner_are_denied()
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync("perpetual");
        f.Store.Change(s => s with { Policy = s.Policy with { IsUnlocked = false } });
        (await f.RunAsync(() => f.Authorization.RevokeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Change(s => s with { Policy = s.Policy with { IsUnlocked = true, OtherMandatoryGatesSatisfied = false } });
        (await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Change(s => s with { Policy = s.Policy with { OtherMandatoryGatesSatisfied = true } });
        f.Time.Now = f.Store.Snapshot.Proposal!.ExpiresAt;
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Time.Now = f.Time.Now.AddMinutes(-1);
        var other = new HostRequest(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi, new(Guid.NewGuid()));
        f.Store.Change(s => s with { Proposal = new(other, s.Proposal!.ProposalId, s.Proposal.Revision,
            s.Proposal.Binding, s.Proposal.Effect, s.Proposal.ExpiresAt) });
        (await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await f.RunAsync(() => f.Authorization.ObserveContentAsync(f.Request, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
    }

    [Fact]
    public async Task Invalid_persisted_approval_schema_and_grant_scope_fail_explicitly()
    {
        using var f = new InteractionFixture();
        var presented = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        f.Store.Change(s => s with { Questions = s.Questions.SetItem(0, s.Questions[0] with
        {
            Spec = new("Corrupt schema", QuestionKind.SingleChoice, [new("invented", "Invented")]),
        }) });
        var corrupt = async () => await f.RunAsync(() => f.Authorization.ApproveAsync(presented.Question!.Key, new(["invented"]), RequestOrigin.LocalUi, CancellationToken.None));
        await corrupt.Should().ThrowAsync<InvalidDataException>();
        f.Store.Snapshot.Grants.Should().BeEmpty();
        f.Store.Change(s => s with { Questions = [] });
        var grant = await f.GrantAsync();
        f.Store.Change(s => s with { Grants = s.Grants.SetItem(0, grant with { Scope = (OperationGrantScope)99 }) });
        var invalidScope = async () => await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None));
        await invalidScope.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Session_reuse_requires_current_revision_but_allows_new_exact_tasks_within_same_active_session()
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync("session");
        f.Rebind();
        var used = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None));
        used.Outcome.Should().Be(HostInteractionOutcome.Consumed);
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        var twice = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, used.Grant!.Revision, CancellationToken.None));
        twice.Grant!.UseCount.Should().Be(2);
    }

    [Fact]
    public async Task Current_source_and_policy_revision_never_inherit_an_old_exact_grant()
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync("perpetual");
        var prior = grant.ApprovedProposal.Binding;
        foreach (var changed in new[]
        {
            new ExactOperationBinding("another-action", prior.SourcePartition, prior.SkillId, prior.DefinitionDigest,
                prior.DeclaredResourceDigest, prior.TrackedContentDigest, prior.ImplementationDigest, prior.InvocationDigest,
                prior.ResourceDigest, prior.IdentityDigest, prior.DestinationDigest, prior.TransformationDigest, prior.PolicyRevision),
            new ExactOperationBinding(prior.ActionId, "other-source", prior.SkillId, prior.DefinitionDigest,
                prior.DeclaredResourceDigest, prior.TrackedContentDigest, prior.ImplementationDigest, prior.InvocationDigest,
                prior.ResourceDigest, prior.IdentityDigest, prior.DestinationDigest, prior.TransformationDigest, prior.PolicyRevision),
            new ExactOperationBinding(prior.ActionId, prior.SourcePartition, "other-skill", prior.DefinitionDigest,
                prior.DeclaredResourceDigest, prior.TrackedContentDigest, prior.ImplementationDigest, prior.InvocationDigest,
                prior.ResourceDigest, prior.IdentityDigest, prior.DestinationDigest, prior.TransformationDigest, prior.PolicyRevision),
            new ExactOperationBinding(prior.ActionId, prior.SourcePartition, prior.SkillId, prior.DefinitionDigest,
                prior.DeclaredResourceDigest, prior.TrackedContentDigest, prior.ImplementationDigest, prior.InvocationDigest,
                prior.ResourceDigest, prior.IdentityDigest, prior.DestinationDigest, prior.TransformationDigest, new(2)),
        })
        {
            f.ChangeProposal(binding: changed);
            (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Denied);
            await f.RunAsync(() => f.Authorization.ObserveContentAsync(f.Request, CancellationToken.None));
            f.Store.Snapshot.Grants[0].Status.Should().Be(OperationGrantStatus.Active);
        }
    }

    private static ExactOperationBinding WebBinding(ExactOperationBinding binding, Uri address) =>
        new(
            WebPageAccessBinding.ActionId,
            binding.SourcePartition,
            binding.SkillId,
            binding.DefinitionDigest,
            binding.DeclaredResourceDigest,
            binding.TrackedContentDigest,
            binding.ImplementationDigest,
            binding.InvocationDigest,
            binding.ResourceDigest,
            binding.IdentityDigest,
            WebPageAccessBinding.DestinationDigest(address),
            binding.TransformationDigest,
            binding.PolicyRevision);

    private sealed class UriConfiguration(
        IEnumerable<string> patterns,
        bool corrupt = false) : IPreapprovedUriConfiguration
    {
        private readonly PreapprovedUriSettings settings = PreapprovedUriSettings.Create(patterns);

        public PreapprovedUriSettings GetSettings() =>
            corrupt ? throw new InvalidDataException("Corrupt preapproval state.") : settings;

        public PreapprovedUriSettings Add(string pattern, Kora.Core.Auditing.SecurityAuditInitiator initiator) =>
            throw new NotSupportedException();

        public PreapprovedUriSettings Remove(string pattern, Kora.Core.Auditing.SecurityAuditInitiator initiator) =>
            throw new NotSupportedException();

        public PreapprovedUriSettings Clear(Kora.Core.Auditing.SecurityAuditInitiator initiator) =>
            throw new NotSupportedException();

        public bool IsPreapproved(Uri uri) => GetSettings().IsPreapproved(uri);
    }
}
