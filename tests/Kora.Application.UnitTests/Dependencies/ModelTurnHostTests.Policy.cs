using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private static ModelProviderPolicy Policy(Fixture f, ModelProviderMode mode = ModelProviderMode.LocalFirst) =>
        new(f.Request.SessionId, new(1), mode, ModelProviderSelection.OllamaCandidate, ModelProviderSelection.CopilotCandidate);

    private static async Task<ModelProviderPolicy> Configure(Fixture f, ModelProviderMode mode = ModelProviderMode.LocalFirst)
    {
        var policy = Policy(f, mode);
        (await f.PolicyHost.SetPolicyAsync(policy, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        return policy;
    }

    private static async Task<ModelHandoffOffer> Offer(Fixture f, ModelProviderPolicy policy, ModelContextEnvelope? context = null)
    {
        var result = await f.Workflow.OfferAsync(policy, context ?? f.Context(), ModelHandoffReason.LocalUnavailable, Token);
        result.Outcome.Should().Be(ModelHandoffOutcome.Offered);
        result.Offer!.Destination.Should().Be(policy.Hosted);
        result.Offer.Reason.Should().Be(ModelHandoffReason.LocalUnavailable);
        return result.Offer;
    }

    [Theory]
    [InlineData(ModelProviderMode.LocalOnly)]
    [InlineData(ModelProviderMode.LocalFirst)]
    [InlineData(ModelProviderMode.HostedPreferred)]
    public async Task Session_modes_are_consumed_by_the_real_one_provider_turn_host(ModelProviderMode mode)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f, mode);
        var admission = await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Default, f.Context(), null, Token);
        if (mode == ModelProviderMode.HostedPreferred)
        {
            admission.Result.Reason.Should().Be(ModelTurnReason.EgressNotAdmitted);
            admission.Turn.Should().BeNull();
        }
        else
        {
            (await f.PolicyHost.RunAsync(admission.Turn!, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            f.Adapter.Calls.Should().Be(1);
        }
        var explicitLocal = await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Local, f.Context(), null, Token);
        explicitLocal.Turn.Should().NotBeNull();
        var hosted = await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Hosted, f.Context(), null, Token);
        hosted.Result.Reason.Should().Be(mode == ModelProviderMode.LocalOnly ? ModelTurnReason.InvalidPolicy
            : mode == ModelProviderMode.LocalFirst ? ModelTurnReason.HandoffReviewRequired : ModelTurnReason.EgressNotAdmitted);
    }

    [Fact]
    public async Task Changed_session_selection_invalidates_admitted_turn_and_old_policy()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var turn = (await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Local, f.Context(), null, Token)).Turn!;
        var replacement = policy with { Revision = new(2), Mode = ModelProviderMode.LocalOnly };
        (await f.PolicyHost.SetPolicyAsync(replacement, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        (await f.PolicyHost.RunAsync(turn, Token)).Reason.Should().Be(ModelTurnReason.PolicyChanged);
        (await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Local, f.Context(), null, Token)).Result.Reason.Should().Be(ModelTurnReason.PolicyChanged);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("foreign")]
    [InlineData("origin")]
    [InlineData("owner")]
    [InlineData("session")]
    [InlineData("inactive")]
    [InlineData("generation")]
    [InlineData("task")]
    [InlineData("revision")]
    public async Task Policy_publication_is_original_user_owned_revisioned_and_fail_closed(string failure)
    {
        using var f = new Fixture(string.Equals(failure, "origin", StringComparison.Ordinal) ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        using var root = f.Root();
        var policy = Policy(f);
        switch (failure)
        {
            case "invalid": policy = policy with { Mode = ModelProviderMode.Unknown }; break;
            case "foreign": policy = policy with { Session = new(Guid.NewGuid()) }; break;
            case "owner": f.Current = false; break;
            case "session": f.Session = f.Session with { SessionId = new(Guid.NewGuid()) }; break;
            case "inactive": f.Session = f.Session with { IsActive = false }; break;
            case "generation": f.Session = f.Session with { Generation = default }; break;
            case "task": f.Task = null; break;
            case "revision": policy = policy with { Revision = new(2) }; break;
        }
        (await f.PolicyHost.SetPolicyAsync(policy, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        (await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Local, f.Context(), null, Token)).Turn.Should().BeNull();
    }

    [Theory]
    [InlineData(ModelHandoffDecision.Approve, ModelHandoffOutcome.Approved)]
    [InlineData(ModelHandoffDecision.Decline, ModelHandoffOutcome.Declined)]
    [InlineData(ModelHandoffDecision.Cancel, ModelHandoffOutcome.Cancelled)]
    public async Task Exact_review_has_typed_terminal_outcomes_and_no_egress_authority(ModelHandoffDecision decision, ModelHandoffOutcome outcome)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var offer = await Offer(f, policy);
        var result = await f.Workflow.ReviewAsync(offer, policy, offer.Context, decision, RequestOrigin.ActivatedVoice, Token);
        result.Outcome.Should().Be(outcome);
        if (decision == ModelHandoffDecision.Approve)
        {
            var admitted = await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Hosted, offer.Context, result.Review, Token);
            admitted.Result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
            admitted.Result.Reason.Should().Be(ModelTurnReason.EgressNotAdmitted);
            (await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Hosted, offer.Context, result.Review, Token))
                .Result.Reason.Should().Be(ModelTurnReason.HandoffReviewStale);
        }
        else { result.Review.Should().BeNull(); }
        (await f.Workflow.ReviewAsync(offer, policy, offer.Context, decision, RequestOrigin.LocalUi, Token))
            .Outcome.Should().Be(ModelHandoffOutcome.Stale);
        f.Adapter.Calls.Should().Be(0);
        f.Questions.Snapshot.Grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData("content")]
    [InlineData("lineage")]
    [InlineData("policy")]
    [InlineData("destination")]
    [InlineData("generation")]
    [InlineData("task")]
    [InlineData("control")]
    [InlineData("expired")]
    [InlineData("origin")]
    [InlineData("decision")]
    [InlineData("unknown")]
    [InlineData("foreign")]
    [InlineData("question")]
    [InlineData("closed")]
    public async Task Changed_or_hostile_review_inputs_never_produce_a_receipt(string change)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var offer = await Offer(f, policy);
        var context = offer.Context;
        var channel = RequestOrigin.LocalUi;
        var decision = ModelHandoffDecision.Approve;
        switch (change)
        {
            case "content": context = f.Context(text: "changed"); break;
            case "lineage": context = f.Context(); break;
            case "policy": policy = policy with { Revision = new(2) }; break;
            case "destination": policy = policy with { Hosted = policy.Hosted with { Revision = new(2) } }; break;
            case "generation": f.Session = f.Session with { Generation = new(2) }; f.Task = f.Task! with { Generation = new(2) }; break;
            case "task": f.Task = f.Task! with { Task = new(f.Request, new(2), f.Task.Task.State) }; break;
            case "control": f.ControlRevision++; break;
            case "expired": f.Time.Now = context.ExpiresAt; break;
            case "origin": channel = RequestOrigin.HostSystem; break;
            case "decision": decision = (ModelHandoffDecision)99; break;
            case "unknown": decision = ModelHandoffDecision.Unknown; break;
            case "foreign":
                f.ExpectedAuditRequest = HostRequest.Create(RequestOrigin.LocalUi);
                using (HostActivity.BeginRoot(f.ExpectedAuditRequest, HostActivityLayer.Application, HostOperation.Request))
                {
                    (await f.Workflow.ReviewAsync(offer, policy, context, decision, channel, Token)).Review.Should().BeNull();
                }
                return;
            case "question": f.Questions.Change(s => s with { Questions = [] }); break;
            case "closed": f.Questions.Change(s => s with { Policy = s.Policy with { IsUnlocked = false } }); break;
        }
        var result = await f.Workflow.ReviewAsync(offer, policy, context, decision, channel, Token);
        result.Review.Should().BeNull();
        result.Outcome.Should().NotBe(ModelHandoffOutcome.Approved);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(ModelProviderMode.LocalOnly)]
    [InlineData(ModelProviderMode.HostedPreferred)]
    public async Task Only_local_first_can_offer_a_hosted_handoff(ModelProviderMode mode)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f, mode);
        (await f.Workflow.OfferAsync(policy, f.Context(), ModelHandoffReason.LocalUnavailable, Token))
            .Reason.Should().Be(ModelTurnReason.HandoffNotPermitted);
        f.Questions.Snapshot.Questions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ModelHandoffReason.Unknown)]
    [InlineData((ModelHandoffReason)99)]
    public async Task Unknown_or_confidence_like_conditions_cannot_offer_disclosure(ModelHandoffReason reason)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        (await f.Workflow.OfferAsync(policy, f.Context(), reason, Token)).Reason.Should().Be(ModelTurnReason.InvalidHandoffReason);
    }

    [Fact]
    public async Task Removal_retires_the_old_review_and_requires_fresh_exact_review()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var evidence = new ModelContextEvidence(new(Guid.NewGuid()), f.Request, new(1), "private excerpt", ModelEvidenceDisclosure.HostedEligible);
        var context = new ModelContextEnvelope(f.Request, "policy", "request", [evidence], f.Time.Now, f.Time.Now.AddMinutes(1));
        var offer = await Offer(f, policy, context);
        var removed = await f.Workflow.RemoveAsync(offer, [evidence.Id], Token);
        removed.Outcome.Should().Be(ModelHandoffOutcome.Removed);
        removed.Offer!.Context.Evidence.Should().BeEmpty();
        removed.Offer.Context.Serialize().Should().NotEqual(context.Serialize());
        removed.Offer.Question.Key.Should().NotBe(offer.Question.Key);
        (await f.Workflow.ReviewAsync(offer, policy, context, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token))
            .Outcome.Should().Be(ModelHandoffOutcome.Stale);
        (await f.Workflow.ReviewAsync(removed.Offer, policy, context, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token))
            .Outcome.Should().Be(ModelHandoffOutcome.Stale);
        (await f.Workflow.ReviewAsync(removed.Offer, policy, removed.Offer.Context, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token))
            .Outcome.Should().Be(ModelHandoffOutcome.Approved);
    }

    [Fact]
    public async Task Local_only_evidence_and_complete_envelope_overflow_are_not_offered()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var evidence = new ModelContextEvidence(new(Guid.NewGuid()), f.Request, new(1), "local secret", ModelEvidenceDisclosure.LocalOnly);
        var local = new ModelContextEnvelope(f.Request, "policy", "request", [evidence], f.Time.Now, f.Time.Now.AddMinutes(1));
        (await f.Workflow.OfferAsync(policy, local, ModelHandoffReason.UserRequestedHosted, Token))
            .Reason.Should().Be(ModelTurnReason.LocalEvidenceNotDisclosable);
        var large = f.Context(text: new string('x', ModelContextEnvelope.MaximumInputUtf8Bytes));
        (await f.Workflow.OfferAsync(policy, large, ModelHandoffReason.ContextBudgetExceeded, Token))
            .Reason.Should().Be(ModelTurnReason.EnvelopeLimitExceeded);
    }

    [Fact]
    public async Task Question_and_terminal_audit_failures_never_return_review_or_publish_policy()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.AuditFailure = true;
        await Assert.ThrowsAsync<IOException>(() => f.PolicyHost.SetPolicyAsync(Policy(f), Token));
        f.AuditFailure = false;
        var policy = await Configure(f);
        var offer = await Offer(f, policy);
        f.Questions.FailAudit = true;
        await Assert.ThrowsAsync<IOException>(() => f.Workflow.ReviewAsync(offer, policy, offer.Context,
            ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token));
        f.Questions.Snapshot.Questions[0].Status.Should().Be(QuestionStatus.Pending);
        f.Questions.FailAudit = false;
        var fresh = await Offer(f, policy);
        f.OnAudit = item => { if (string.Equals(item.ActionId, "model.handoff", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException("terminal unavailable"); } };
        await Assert.ThrowsAsync<IOException>(() => f.Workflow.ReviewAsync(fresh, policy, fresh.Context,
            ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token));
        f.Audits.Last().Outcome.Should().Be(SecurityAuditOutcome.Unknown);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Cancellation_and_authority_change_during_question_commit_close_late_review()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var offer = await Offer(f, policy);
        using var cancellation = new CancellationTokenSource();
        f.Questions.BeforeCommit = cancellation.Cancel;
        (await f.Workflow.ReviewAsync(offer, policy, offer.Context, ModelHandoffDecision.Approve,
            RequestOrigin.LocalUi, cancellation.Token)).Outcome.Should().Be(ModelHandoffOutcome.Cancelled);
        f.Questions.Snapshot.Questions[0].Status.Should().Be(QuestionStatus.Pending);
        f.Questions.BeforeCommit = null;
        offer = await Offer(f, policy);
        f.Questions.BeforeCommit = () => f.ControlRevision++;
        (await f.Workflow.ReviewAsync(offer, policy, offer.Context, ModelHandoffDecision.Approve,
            RequestOrigin.LocalUi, Token)).Review.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("stopped")]
    [InlineData("completed")]
    public async Task Missing_or_closed_host_context_cannot_configure_offer_or_admit_policy(string state)
    {
        using var f = new Fixture();
        using var root = string.Equals(state, "none", StringComparison.Ordinal) ? null : f.Root();
        if (string.Equals(state, "stopped", StringComparison.Ordinal)) { root!.Activity!.Stop(); }
        if (string.Equals(state, "completed", StringComparison.Ordinal)) { root!.Complete(HostOperationOutcome.Failed); }
        var policy = Policy(f);
        (await f.PolicyHost.SetPolicyAsync(policy, Token)).Reason.Should().Be(ModelTurnReason.HostContextRequired);
        (await f.Workflow.OfferAsync(policy, f.Context(), ModelHandoffReason.LocalUnavailable, Token))
            .Reason.Should().Be(ModelTurnReason.HostContextRequired);
        (await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Local, f.Context(), null, Token))
            .Result.Reason.Should().Be(ModelTurnReason.HostContextRequired);
        (await f.PolicyHost.ObserveHandoffAsync(policy, f.Context(), Token)).Reason.Should().Be(ModelTurnReason.HostContextRequired);
        f.Audits.Should().BeEmpty();
    }

    [Theory]
    [InlineData("cancel-before")]
    [InlineData("cancel-read")]
    [InlineData("error-read")]
    [InlineData("cancel-audit")]
    [InlineData("owner-audit")]
    [InlineData("reenter-audit")]
    public async Task Policy_publication_fences_cancellation_failures_and_reentrant_audit(string change)
    {
        using var f = new Fixture();
        using var root = f.Root();
        using var cancellation = new CancellationTokenSource();
        var policy = Policy(f);
        switch (change)
        {
            case "cancel-before": cancellation.Cancel(); break;
            case "cancel-read": f.OnRead = () => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }; break;
            case "error-read": f.OnRead = () => throw new IOException("unavailable"); break;
            case "cancel-audit":
                f.OnAudit = item => { if (item.Outcome == SecurityAuditOutcome.Succeeded) { cancellation.Cancel(); } };
                break;
            case "owner-audit":
                f.OnAudit = item => { if (item.Outcome == SecurityAuditOutcome.Succeeded) { f.Current = false; } };
                break;
            case "reenter-audit":
                f.OnAudit = item =>
                {
                    if (item.Outcome == SecurityAuditOutcome.Succeeded)
                    {
                        f.OnAudit = null;
                        f.PolicyHost.SetPolicyAsync(policy with { Mode = ModelProviderMode.LocalOnly }, Token)
                            .IsCompletedSuccessfully.Should().BeTrue();
                    }
                };
                break;
        }
        if (string.Equals(change, "error-read", StringComparison.Ordinal))
        {
            await Assert.ThrowsAsync<IOException>(() => f.PolicyHost.SetPolicyAsync(policy, cancellation.Token));
        }
        else { (await f.PolicyHost.SetPolicyAsync(policy, cancellation.Token)).Outcome.Should().NotBe(ModelTurnOutcome.Succeeded); }
    }

    [Theory]
    [InlineData("origin", ModelTurnReason.OriginalUserRequired)]
    [InlineData("policy", ModelTurnReason.PolicyChanged)]
    [InlineData("owner", ModelTurnReason.HostAdmissionClosed)]
    [InlineData("session", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("inactive", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("generation", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("task", ModelTurnReason.SessionNotAdmitted)]
    [InlineData("read-policy", ModelTurnReason.HostAdmissionClosed)]
    [InlineData("read-expiry", ModelTurnReason.ContextExpired)]
    [InlineData("unregistered", ModelTurnReason.UnregisteredModel)]
    public async Task Handoff_reobserves_every_prerequisite_without_provider_contact(string change, ModelTurnReason expected)
    {
        using var f = new Fixture(string.Equals(change, "origin", StringComparison.Ordinal) ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        using var root = f.Root();
        var policy = Policy(f);
        if (string.Equals(change, "unregistered", StringComparison.Ordinal))
        {
            policy = policy with { Hosted = policy.Hosted with { Model = new(Guid.NewGuid()) } };
        }
        if (!string.Equals(change, "origin", StringComparison.Ordinal))
        {
            (await f.PolicyHost.SetPolicyAsync(policy, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        }
        switch (change)
        {
            case "policy": policy = policy with { Revision = new(2) }; break;
            case "owner": f.Current = false; break;
            case "session": f.Session = f.Session with { SessionId = new(Guid.NewGuid()) }; break;
            case "inactive": f.Session = f.Session with { IsActive = false }; break;
            case "generation": f.Session = f.Session with { Generation = default }; break;
            case "task": f.Task = null; break;
            case "read-policy":
                f.OnRead = () =>
                {
                    f.OnRead = null;
                    f.PolicyHost.SetPolicyAsync(policy with { Revision = new(2) }, Token).IsCompletedSuccessfully.Should().BeTrue();
                };
                break;
            case "read-expiry": f.OnRead = () => f.Time.Now = DateTimeOffset.UnixEpoch.AddMinutes(1); break;
        }
        (await f.Workflow.OfferAsync(policy, f.Context(), ModelHandoffReason.LocalUnavailable, Token)).Reason.Should().Be(expected);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Review_is_not_qualification_and_foreign_host_offers_cannot_be_consumed()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var production = f.Production();
        (await production.SetPolicyAsync(policy, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var workflow = new ModelProviderHandoffWorkflow(production, new(f.Questions, f.Time), f, NullLogger<ModelProviderHandoffWorkflow>.Instance);
        (await workflow.OfferAsync(policy, f.Context(), ModelHandoffReason.LocalUnavailable, Token))
            .Outcome.Should().Be(ModelHandoffOutcome.Unavailable);
        var offer = await Offer(f, policy);
        (await workflow.ReviewAsync(offer, policy, offer.Context, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token))
            .Reason.Should().Be(ModelTurnReason.HandoffReviewStale);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("question-denied")]
    [InlineData("offer-changed")]
    [InlineData("question-expired")]
    [InlineData("terminal-changed")]
    [InlineData("terminal-cancel")]
    public async Task Question_and_terminal_audit_races_return_no_review_capability(string change)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        if (string.Equals(change, "question-denied", StringComparison.Ordinal))
        {
            f.Questions.Change(s => s with { Policy = s.Policy with { IsUnlocked = false } });
            (await f.Workflow.OfferAsync(policy, f.Context(), ModelHandoffReason.LocalUnavailable, Token)).Offer.Should().BeNull();
            return;
        }
        if (string.Equals(change, "offer-changed", StringComparison.Ordinal))
        {
            f.Questions.BeforeCommit = () => f.ControlRevision++;
            (await f.Workflow.OfferAsync(policy, f.Context(), ModelHandoffReason.LocalUnavailable, Token)).Offer.Should().BeNull();
            return;
        }
        var offer = await Offer(f, policy);
        using var cancellation = new CancellationTokenSource();
        if (string.Equals(change, "question-expired", StringComparison.Ordinal))
        {
            f.Questions.BeforeTransition = () => f.Time.Now = offer.Context.ExpiresAt;
        }
        else
        {
            f.OnAudit = item =>
            {
                if (string.Equals(item.ActionId, "model.handoff", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Succeeded)
                {
                    if (string.Equals(change, "terminal-cancel", StringComparison.Ordinal)) { cancellation.Cancel(); }
                    else { f.ControlRevision++; }
                }
            };
        }
        var result = await f.Workflow.ReviewAsync(offer, policy, offer.Context, ModelHandoffDecision.Approve,
            RequestOrigin.LocalUi, cancellation.Token);
        result.Review.Should().BeNull();
        if (string.Equals(change, "question-expired", StringComparison.Ordinal)) { result.Outcome.Should().Be(ModelHandoffOutcome.Expired); }
    }

    [Fact]
    public async Task Review_binding_is_rechecked_at_hosted_admission_not_just_at_confirmation()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var offer = await Offer(f, policy);
        var review = (await f.Workflow.ReviewAsync(offer, policy, offer.Context, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token)).Review!;
        (await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Hosted, f.Context(), review, Token))
            .Result.Reason.Should().Be(ModelTurnReason.HandoffReviewStale);
        f.Time.Now = offer.Context.ExpiresAt;
        (await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Hosted, offer.Context, review, Token))
            .Result.Reason.Should().Be(ModelTurnReason.ContextExpired);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("stale")]
    [InlineData("expired")]
    [InlineData("question")]
    [InlineData("replacement")]
    public async Task Removal_does_not_reuse_authority_or_accept_invalid_items(string change)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var evidence = new ModelContextEvidence(new(Guid.NewGuid()), f.Request, new(1), "excerpt", ModelEvidenceDisclosure.HostedEligible);
        var context = new ModelContextEnvelope(f.Request, "policy", "request", [evidence], f.Time.Now, f.Time.Now.AddMinutes(1));
        var offer = await Offer(f, policy, context);
        IReadOnlyCollection<HostId<EvidenceIdentity>> remove = [evidence.Id];
        switch (change)
        {
            case "empty": remove = []; break;
            case "duplicate": remove = [evidence.Id, evidence.Id]; break;
            case "unknown": remove = [new(Guid.NewGuid())]; break;
            case "stale":
                await f.Workflow.ReviewAsync(offer, policy, context, ModelHandoffDecision.Decline, RequestOrigin.LocalUi, Token);
                break;
            case "expired": f.Time.Now = context.ExpiresAt; break;
            case "question": f.Questions.Change(s => s with { Questions = [] }); break;
            case "replacement": f.Questions.BeforeCommit = () => f.ControlRevision++; break;
        }
        (await f.Workflow.RemoveAsync(offer, remove, Token)).Outcome.Should().NotBe(ModelHandoffOutcome.Removed);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable, true)]
    [InlineData(ModelTurnOutcome.Succeeded, false)]
    [InlineData(ModelTurnOutcome.TimedOut, false)]
    public async Task Host_audited_local_outcome_can_offer_but_never_silently_dispatch_hosted(ModelTurnOutcome outcome, bool offered)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var turn = (await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Local, f.Context(), null, Token)).Turn!;
        (await f.Workflow.OfferAfterLocalAsync(turn, f.Context(), Token)).Outcome.Should().Be(ModelHandoffOutcome.Denied);
        f.Adapter.Reply = new(outcome, policy.Local, new("answer", null));
        await f.PolicyHost.RunAsync(turn, Token);
        var result = await f.Workflow.OfferAfterLocalAsync(turn, f.Context(), Token);
        result.Outcome.Should().Be(offered ? ModelHandoffOutcome.Offered : ModelHandoffOutcome.Denied);
        f.Adapter.Calls.Should().Be(1);
        var foreign = f.Qualified(policy.Local);
        var unbound = (await foreign.AdmitAsync(policy.Local, f.Context(), Token)).Turn!;
        (await f.Workflow.OfferAfterLocalAsync(unbound, f.Context(), Token)).Outcome.Should().Be(ModelHandoffOutcome.Denied);
    }

    [Fact]
    public async Task A_session_policy_cannot_select_a_provider_for_another_session()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var foreign = HostRequest.Create(RequestOrigin.LocalUi);
        f.ExpectedAuditRequest = foreign;
        using var other = HostActivity.BeginRoot(foreign, HostActivityLayer.Application, HostOperation.Request);
        (await f.PolicyHost.AdmitAsync(policy, ModelTurnChoice.Local, f.Context(foreign), null, Token))
            .Result.Reason.Should().Be(ModelTurnReason.InvalidPolicy);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(32768, ModelHandoffOutcome.Offered)]
    [InlineData(32769, ModelHandoffOutcome.Denied)]
    public async Task Review_offer_counts_the_exact_complete_serialized_envelope(int bytes, ModelHandoffOutcome outcome)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var framing = f.Context(text: "").Serialize().Length;
        var context = f.Context(text: new string('x', bytes - framing));
        context.Serialize().Length.Should().Be(bytes);
        (await f.Workflow.OfferAsync(policy, context, ModelHandoffReason.UserRequestedHosted, Token)).Outcome.Should().Be(outcome);
    }

    [Fact]
    public async Task Deferred_review_uses_the_live_original_user_context_not_a_completed_activity()
    {
        using var f = new Fixture();
        ModelProviderPolicy policy;
        ModelHandoffOffer offer;
        using (var original = f.Root())
        {
            policy = await Configure(f);
            offer = await Offer(f, policy);
            original.Complete(HostOperationOutcome.Completed);
        }
        using var resumed = f.Root();
        var result = await f.Workflow.ReviewAsync(offer, policy, offer.Context, ModelHandoffDecision.Approve, RequestOrigin.LocalUi, Token);
        result.Outcome.Should().Be(ModelHandoffOutcome.Approved);
        HostActivity.Current.Should().BeSameAs(resumed);
        f.Messages.Should().NotContain(message => message.Contains(offer.Context.UserRequest, StringComparison.Ordinal));
        f.Audits.Should().OnlyContain(item => item.ActionId.StartsWith("model.", StringComparison.Ordinal));
    }
}
