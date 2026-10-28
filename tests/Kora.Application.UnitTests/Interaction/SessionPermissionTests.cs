using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class SessionPermissionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(SessionPermissionLevel.ReviewAll, HostInteractionOutcome.Presented)]
    [InlineData(SessionPermissionLevel.ModelReview, HostInteractionOutcome.Presented)]
    [InlineData(SessionPermissionLevel.ApproveAll, HostInteractionOutcome.Approved)]
    public async Task Exact_session_level_controls_only_ungranted_ordinary_actions(
        SessionPermissionLevel level, HostInteractionOutcome outcome)
    {
        using var f = new InteractionFixture();
        SetPolicy(f, level);
        var decision = await Authorize(f);
        decision.Outcome.Should().Be(outcome);
        f.Store.Snapshot.Grants.Should().BeEmpty();
        if (outcome == HostInteractionOutcome.Approved)
        {
            decision.Admission!.Basis.Should().Be(SessionPermissionBasis.ApproveAll);
            decision.Admission.Policy.Should().Be(f.Store.Snapshot.EffectivePermission);
            decision.Admission.Proposal.Should().Be(f.Store.Snapshot.Proposal);
            f.Store.Snapshot.Questions.Should().BeEmpty();
        }
        else { f.Store.Snapshot.Questions.Should().ContainSingle(); }
        f.Store.Commits.Should().ContainSingle().Which.Audit.ReasonCode.Should().Be(decision.Reason);
    }

    [Theory]
    [InlineData(SessionPermissionLevel.ReviewAll)]
    [InlineData(SessionPermissionLevel.ModelReview)]
    [InlineData(SessionPermissionLevel.ApproveAll)]
    public async Task Independent_consent_requires_user_and_never_invokes_reviewer(SessionPermissionLevel level)
    {
        using var f = new InteractionFixture();
        SetPolicy(f, level);
        var reviewer = new Reviewer();
        var decision = await Authorize(f, reviewer, consent: true);
        decision.Outcome.Should().Be(HostInteractionOutcome.Presented);
        reviewer.Calls.Should().Be(0);
        f.Store.Snapshot.Admissions.Should().BeEmpty();
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData("once")]
    [InlineData("session")]
    [InlineData("perpetual")]
    public async Task Exact_grant_wins_without_model_inference_or_new_grant(string scope)
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync(scope);
        SetPolicy(f, SessionPermissionLevel.ModelReview);
        var reviewer = new Reviewer();
        var decision = await Authorize(f, reviewer);
        decision.Outcome.Should().Be(HostInteractionOutcome.Consumed);
        decision.Admission!.Basis.Should().Be(SessionPermissionBasis.ExistingGrant);
        decision.Admission.GrantId.Should().Be(grant.Id);
        reviewer.Calls.Should().Be(0);
        f.Store.Snapshot.Grants.Should().ContainSingle().Which.UseCount.Should().Be(1);
    }

    [Theory]
    [InlineData(SessionPermissionVerdict.Safe, HostInteractionOutcome.Approved)]
    [InlineData(SessionPermissionVerdict.Concern, HostInteractionOutcome.Presented)]
    [InlineData(SessionPermissionVerdict.Uncertain, HostInteractionOutcome.Presented)]
    public async Task Separate_tools_disabled_review_is_invocation_bound(
        SessionPermissionVerdict verdict, HostInteractionOutcome outcome)
    {
        using var f = new InteractionFixture();
        SetPolicy(f, SessionPermissionLevel.ModelReview);
        var reviewer = new Reviewer { Verdict = verdict };
        var decision = await Authorize(f, reviewer);
        decision.Outcome.Should().Be(outcome);
        var request = reviewer.Request!;
        request.Proposal.Should().Be(f.Store.Snapshot.Proposal);
        request.Policy.Should().Be(f.Store.Snapshot.EffectivePermission);
        request.ProtocolVersion.Should().Be(1);
        request.ToolsEnabled.Should().BeFalse();
        request.ApprovalCallbacksEnabled.Should().BeFalse();
        request.ConversationContextEnabled.Should().BeFalse();
        request.HostedFallbackEnabled.Should().BeFalse();
        f.Store.Snapshot.Grants.Should().BeEmpty();
        if (outcome == HostInteractionOutcome.Approved)
        { decision.Admission!.Basis.Should().Be(SessionPermissionBasis.ModelSafe); }
    }

    [Theory]
    [InlineData(0, "reviewer-unavailable-user-approval-required")]
    [InlineData(1, "reviewer-invalid-user-approval-required")]
    [InlineData(2, "reviewer-invalid-user-approval-required")]
    [InlineData(3, "reviewer-failed-user-approval-required")]
    [InlineData(4, "reviewer-timeout-user-approval-required")]
    [InlineData(5, "reviewer-invalid-user-approval-required")]
    public async Task Failed_or_unqualified_reviewer_never_supplies_authority(int failure, string reason)
    {
        using var f = new InteractionFixture();
        SetPolicy(f, SessionPermissionLevel.ModelReview);
        var reviewer = new Reviewer
        {
            IsQualified = failure != 0,
            Result = request => failure switch
            {
                1 => new(Guid.NewGuid(), SessionPermissionVerdict.Safe, "local-v1", "bounded"),
                2 => new(request.Id, (SessionPermissionVerdict)99, "local-v1", "bounded"),
                3 => throw new IOException("Transport failed."),
                4 => throw new OperationCanceledException("Reviewer budget ended."),
                5 => null!,
                _ => new(request.Id, SessionPermissionVerdict.Safe, "local-v1", "bounded"),
            },
        };
        var decision = await Authorize(f, reviewer);
        decision.Outcome.Should().Be(HostInteractionOutcome.Presented);
        decision.Reason.Should().Be(reason);
        f.Store.Snapshot.Admissions.Should().BeEmpty();
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Mandatory_gate_failure_cannot_be_autoapproved(int gate)
    {
        using var f = new InteractionFixture();
        SetPolicy(f, SessionPermissionLevel.ApproveAll);
        f.Store.Change(snapshot => gate switch
        {
            0 => snapshot with { Policy = snapshot.Policy with { IsUnlocked = false } },
            1 => snapshot with { Policy = snapshot.Policy with { OtherMandatoryGatesSatisfied = false } },
            2 => snapshot with { Proposal = null },
            3 => snapshot with { Proposal = new(f.Request, snapshot.Proposal!.ProposalId, new(1),
                snapshot.Proposal.Binding, HostOperationEffect.Prohibited, snapshot.Proposal.ExpiresAt) },
            _ => snapshot with { Proposal = new(f.Request, snapshot.Proposal!.ProposalId, new(1),
                snapshot.Proposal.Binding, snapshot.Proposal.Effect, f.Time.Now) },
        });
        (await Authorize(f)).Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Snapshot.Admissions.Should().BeEmpty();
        f.Store.Snapshot.Grants.Should().BeEmpty();
        f.Store.Snapshot.Questions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Late_safe_after_policy_content_lifecycle_or_privacy_change_has_no_authority(int change)
    {
        using var f = new InteractionFixture();
        SetPolicy(f, SessionPermissionLevel.ModelReview);
        var reviewer = new Reviewer
        {
            Result = request =>
            {
                f.Store.Change(snapshot => change switch
                {
                    0 => snapshot with { Permission = snapshot.EffectivePermission with { Revision = new(2) } },
                    1 => snapshot with { Proposal = new(f.Request, snapshot.Proposal!.ProposalId, new(2),
                        snapshot.Proposal.Binding, snapshot.Proposal.Effect, snapshot.Proposal.ExpiresAt) },
                    2 => snapshot with { Session = snapshot.Session with { IsActive = false } },
                    _ => snapshot with { Policy = snapshot.Policy with { IsUnlocked = false } },
                });
                if (change == 2) { SetPolicy(f, SessionPermissionLevel.ReviewAll); }
                return new(request.Id, SessionPermissionVerdict.Safe, "local-v1", "bounded");
            },
        };
        (await Authorize(f, reviewer)).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        f.Store.Snapshot.Admissions.Should().BeEmpty();
        f.Store.Snapshot.Questions.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_never_opens_late_prompt_or_saves_safe_result()
    {
        using var f = new InteractionFixture();
        using var cancellation = new CancellationTokenSource();
        SetPolicy(f, SessionPermissionLevel.ModelReview);
        var reviewer = new Reviewer { Result = _ => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); } };
        await FluentActions.Awaiting(() => f.RunAsync(() =>
            new HostAuthorizationService(f.Store, f.Time, reviewer: reviewer)
                .AuthorizeAsync(f.Request, false, cancellation.Token))).Should().ThrowAsync<OperationCanceledException>();
        f.Store.Snapshot.Admissions.Should().BeEmpty();
        f.Store.Snapshot.Questions.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_is_single_use_and_policy_change_retires_undispatched_receipt()
    {
        using var f = new InteractionFixture();
        SetPolicy(f, SessionPermissionLevel.ApproveAll);
        var admitted = (await Authorize(f)).Admission!;
        (await Authorize(f)).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        var first = await f.RunAsync(() => f.Authorization.DispatchAsync(f.Request, admitted, Token));
        first.Outcome.Should().Be(HostInteractionOutcome.Consumed);
        first.Admission!.Dispatched.Should().BeTrue();
        (await f.RunAsync(() => f.Authorization.DispatchAsync(f.Request, admitted, Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        (await f.RunAsync(() => f.Authorization.DispatchAsync(f.Request, first.Admission, Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        (await f.RunAsync(() => f.Authorization.ValidateAdmissionAsync(f.Request, first.Admission, Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Approved);
        f.Store.Change(snapshot => snapshot with { Permission = snapshot.EffectivePermission with { Revision = new(2) } });
        (await f.RunAsync(() => f.Authorization.ValidateAdmissionAsync(f.Request, first.Admission, Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task Fabricated_receipts_cross_session_policy_and_audit_failure_fail_closed()
    {
        using var f = new InteractionFixture();
        SetPolicy(f, SessionPermissionLevel.ApproveAll);
        var fabricated = new SessionPermissionAdmission(Guid.NewGuid(), f.Store.Snapshot.Proposal!,
            f.Store.Snapshot.EffectivePermission, SessionPermissionBasis.ApproveAll);
        (await f.RunAsync(() => f.Authorization.DispatchAsync(f.Request, fabricated, Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        f.Store.Change(snapshot => snapshot with
        {
            Permission = snapshot.EffectivePermission with { SessionId = new(Guid.NewGuid()) },
        });
        await FluentActions.Awaiting(() => Authorize(f)).Should().ThrowAsync<InvalidDataException>();
        SetPolicy(f, SessionPermissionLevel.ApproveAll);
        f.Store.FailAudit = true;
        await FluentActions.Awaiting(() => Authorize(f)).Should().ThrowAsync<IOException>();
        f.Store.Snapshot.Admissions.Should().BeEmpty();
    }

    [Fact]
    public async Task Reusable_grant_has_one_receipt_per_invocation_and_revocation_retires_its_use()
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync("session");
        var used = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, Token));
        var duplicate = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, used.Grant!.Revision, Token));
        duplicate.Reason.Should().Be("invocation-already-admitted");
        (await f.RunAsync(() => f.Authorization.ValidateAdmissionAsync(f.Request, used.Admission!, Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Approved);
        await f.RunAsync(() => f.Authorization.RevokeAsync(f.Request, grant.Id, used.Grant!.Revision, Token));
        (await f.RunAsync(() => f.Authorization.DispatchAsync(f.Request, used.Admission!, Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        f.Store.Snapshot.Grants.Single().UseCount.Should().Be(1);
    }

    [Fact]
    public async Task Safe_returned_after_the_review_budget_ended_requires_user_approval()
    {
        using var f = new InteractionFixture();
        SetPolicy(f, SessionPermissionLevel.ModelReview);
        var clock = new ReviewClock(f.Time);
        var reviewer = new Reviewer
        {
            Result = request =>
            {
                clock.Fire();
                return new(request.Id, SessionPermissionVerdict.Safe, "local-v1", "bounded");
            },
        };
        var result = await f.RunAsync(() => new HostAuthorizationService(f.Store, clock, reviewer: reviewer)
            .AuthorizeAsync(f.Request, false, Token));
        result.Reason.Should().Be("reviewer-timeout-user-approval-required");
        f.Store.Snapshot.Admissions.Should().BeEmpty();
    }

    [Fact]
    public async Task Reviewer_qualification_lost_before_commit_requires_explicit_user_approval()
    {
        using var f = new InteractionFixture();
        SetPolicy(f, SessionPermissionLevel.ModelReview);
        var reviewer = new Reviewer();
        reviewer.Result = request =>
        {
            reviewer.IsQualified = false;
            return new(request.Id, SessionPermissionVerdict.Safe, "local-v1", "bounded");
        };
        var result = await Authorize(f, reviewer);
        result.Outcome.Should().Be(HostInteractionOutcome.Presented);
        result.Reason.Should().Be("reviewer-unavailable-user-approval-required");
        f.Store.Snapshot.Admissions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("invalid content", false)]
    [InlineData("bad/", false)]
    [InlineData("v1-alpha_test.0", true)]
    public void Reviewer_labels_are_bounded_inert_protocol_fields(string value, bool valid)
    {
        using var f = new InteractionFixture();
        var request = new SessionPermissionReviewRequest(Guid.NewGuid(), f.Store.Snapshot.Proposal!, f.Store.Snapshot.EffectivePermission);
        new SessionPermissionReviewResult(request.Id, SessionPermissionVerdict.Safe, value, "bounded")
            .IsValidFor(request).Should().Be(valid);
        new SessionPermissionReviewResult(request.Id, SessionPermissionVerdict.Safe, "local", value)
            .IsValidFor(request).Should().Be(valid);
        new SessionPermissionReviewResult(request.Id, SessionPermissionVerdict.Safe, new string('a', 81), "bounded")
            .IsValidFor(request).Should().BeFalse();
    }

    [Theory]
    [InlineData(SessionPermissionLevel.ReviewAll, "Review all")]
    [InlineData(SessionPermissionLevel.ModelReview, "Model review")]
    [InlineData(SessionPermissionLevel.ApproveAll, "Approve all")]
    public void Policy_names_and_corrupt_saved_values_are_explicit(SessionPermissionLevel level, string name)
    {
        using var f = new InteractionFixture();
        var policy = f.Store.Snapshot.EffectivePermission with { Level = level };
        policy.DisplayName.Should().Be(name);
        policy.Validate(f.Store.Snapshot.Session);
        FluentActions.Invoking(() => (policy with { Revision = default }).Validate(f.Store.Snapshot.Session))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => (policy with { Generation = new(2) }).Validate(f.Store.Snapshot.Session))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => (policy with { Level = (SessionPermissionLevel)99 }).Validate(f.Store.Snapshot.Session))
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => _ = (policy with { Level = (SessionPermissionLevel)99 }).DisplayName)
            .Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => (policy with { Level = SessionPermissionLevel.ApproveAll })
            .Validate(f.Store.Snapshot.Session with { IsActive = false })).Should().Throw<InvalidDataException>();
    }

    private static void SetPolicy(InteractionFixture f, SessionPermissionLevel level) =>
        f.Store.Change(snapshot => snapshot with { Permission = SessionPermissionPolicy.Default(snapshot.Session) with { Level = level } });

    [Fact]
    public async Task A_saved_session_grant_never_matches_a_resumed_generation()
    {
        using var f = new InteractionFixture();
        await f.GrantAsync("session");
        f.Rebind();
        f.Store.Change(snapshot => snapshot with { Session = snapshot.Session with { Generation = new(2) } });
        (await Authorize(f)).Outcome.Should().Be(HostInteractionOutcome.Presented);
        f.Store.Snapshot.Grants.Single().UseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Grant_precedence_requires_complete_current_authority_not_an_active_named_record(int fault)
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync(fault == 3 ? "once" : fault == 4 ? "session" : "perpetual");
        if (fault == 0) { await f.RunAsync(() => f.Authorization.RevokeAsync(f.Request, grant.Id, grant.Revision, Token)); }
        f.Rebind(newSession: fault == 4);
        if (fault == 1) { f.ChangeProposal(binding: InteractionFixture.Binding(changedField: 4)); }
        if (fault == 2) { f.ChangeProposal(effect: HostOperationEffect.BoundedWrite); }
        (await Authorize(f)).Outcome.Should().Be(HostInteractionOutcome.Presented);
        f.Store.Snapshot.Admissions.Should().BeEmpty();
        f.Store.Snapshot.Grants.Single().UseCount.Should().Be(0);
    }

    [Fact]
    public async Task Reviewer_cancellation_waits_for_actual_quiescence_and_never_presents_late_authority()
    {
        using var f = new InteractionFixture();
        using var cancellation = new CancellationTokenSource();
        SetPolicy(f, SessionPermissionLevel.ModelReview);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reviewer = new Reviewer
        {
            AsyncResult = async (request, token) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(Token);
                token.ThrowIfCancellationRequested();
                return new(request.Id, SessionPermissionVerdict.Safe, "local-v1", "bounded");
            },
        };
        var operation = f.RunAsync(() => new HostAuthorizationService(f.Store, f.Time, reviewer: reviewer)
            .AuthorizeAsync(f.Request, false, cancellation.Token));
        await entered.Task.WaitAsync(Token);
        await cancellation.CancelAsync();
        operation.IsCompleted.Should().BeFalse();
        f.Store.Snapshot.Admissions.Should().BeEmpty();
        release.SetResult();
        await FluentActions.Awaiting(() => operation).Should().ThrowAsync<OperationCanceledException>();
        f.Store.Snapshot.Questions.Should().BeEmpty();
    }

    [Fact]
    public async Task An_existing_question_or_late_conflicting_review_cannot_be_autoapproved()
    {
        using var f = new InteractionFixture();
        SetPolicy(f, SessionPermissionLevel.ModelReview);
        var reviewer = new Reviewer
        {
            AsyncResult = async (request, _) =>
            {
                await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, Token));
                return new(request.Id, SessionPermissionVerdict.Safe, "local-v1", "bounded");
            },
        };
        (await Authorize(f, reviewer)).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        SetPolicy(f, SessionPermissionLevel.ApproveAll);
        (await Authorize(f)).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        f.Store.Snapshot.Admissions.Should().BeEmpty();
    }

    private static Task<HostInteractionDecision> Authorize(InteractionFixture f, ISessionPermissionReviewer? reviewer = null, bool consent = false) =>
        f.RunAsync(() => new HostAuthorizationService(f.Store, f.Time, reviewer: reviewer).AuthorizeAsync(f.Request, consent, Token));

    private sealed class Reviewer : ISessionPermissionReviewer
    {
        public bool IsQualified { get; set; } = true;
        public int Calls { get; private set; }
        public SessionPermissionVerdict Verdict { get; init; } = SessionPermissionVerdict.Safe;
        public Func<SessionPermissionReviewRequest, SessionPermissionReviewResult>? Result { get; set; }
        public Func<SessionPermissionReviewRequest, CancellationToken, ValueTask<SessionPermissionReviewResult>>? AsyncResult { get; init; }
        public SessionPermissionReviewRequest? Request { get; private set; }
        public ValueTask<SessionPermissionReviewResult> ReviewAsync(SessionPermissionReviewRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Request = request;
            if (AsyncResult is not null) { return AsyncResult(request, cancellationToken); }
            return ValueTask.FromResult(Result is null
                ? new(request.Id, Verdict, "local-v1", "bounded") : Result(request));
        }
    }

    private sealed class ReviewClock(TimeProvider baseline) : TimeProvider
    {
        private TimerCallback? callback;
        private object? state;
        public override DateTimeOffset GetUtcNow() => baseline.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback;
            this.state = state;
            return new BudgetTimer();
        }
        internal void Fire() => callback!(state);
        private sealed class BudgetTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
