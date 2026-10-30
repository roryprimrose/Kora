using AwesomeAssertions;

using Kora.Application.Context;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class PendingOperationExplanationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(SessionPermissionLevel.ReviewAll, RequestOrigin.LocalUi)]
    [InlineData(SessionPermissionLevel.ModelReview, RequestOrigin.LocalUi)]
    [InlineData(SessionPermissionLevel.ApproveAll, RequestOrigin.LocalUi)]
    [InlineData(SessionPermissionLevel.ReviewAll, RequestOrigin.ActivatedVoice)]
    [InlineData(SessionPermissionLevel.ModelReview, RequestOrigin.ActivatedVoice)]
    [InlineData(SessionPermissionLevel.ApproveAll, RequestOrigin.ActivatedVoice)]
    public async Task CurrentPendingReadReportsIndependentNativePermissionWithoutChangingApproval(
        SessionPermissionLevel level, RequestOrigin origin)
    {
        using var f = Fixture(origin);
        f.Store.Change(s => s with { Permission = s.Permission! with { Level = level } });
        var question = await Present(f);
        var before = f.Store.Snapshot;
        var result = await Explain(f, question);
        result.Outcome.Should().Be(HostInteractionOutcome.Presented);
        result.Reason.Should().Be("clipboard-read-exact-grant-required");
        var observation = result.Explanation!;
        observation.Question.Should().Be(question.Key);
        observation.SessionGeneration.Should().Be(question.SessionGeneration);
        observation.QuestionExpiresAt.Should().Be(question.ExpiresAt);
        observation.Proposal.Should().Be(before.Proposal);
        observation.SessionPolicy.Level.Should().Be(level);
        observation.Gates.Should().Be(before.Policy);
        observation.ApplicableGrant.Should().BeNull();
        result.Question.Should().BeNull();
        result.Grant.Should().BeNull();
        result.Admission.Should().BeNull();
        f.Store.Snapshot.Should().BeSameAs(before);
        (await Explain(f, question, observation)).Explanation.Should().Be(observation);
        f.Store.Snapshot.Should().BeSameAs(before);
        f.Store.Commits.Skip(1).Should().OnlyContain(commit =>
            string.Equals(commit.Audit.ActionId, "approval.explain-pending-clipboard-read", StringComparison.Ordinal)
            && commit.Audit.Outcome == SecurityAuditOutcome.Succeeded
            && string.Equals(commit.Audit.ReasonCode, "clipboard-read-exact-grant-required", StringComparison.Ordinal));

        var approved = await f.RunAsync(() => f.Authorization.ApproveAsync(question.Key, new(["once"]),
            RequestOrigin.LocalUi, Token));
        approved.Outcome.Should().Be(HostInteractionOutcome.Approved);
        f.Store.Snapshot.Admissions.Should().BeEmpty();
        var consumed = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, approved.Grant!.Id,
            approved.Grant.Revision, Token));
        consumed.Outcome.Should().Be(HostInteractionOutcome.Consumed);
        consumed.Grant!.UseCount.Should().Be(1);
    }

    [Theory]
    [InlineData(OperationGrantScope.Once)]
    [InlineData(OperationGrantScope.Session)]
    [InlineData(OperationGrantScope.Perpetual)]
    public async Task ObservedExactGrantIsNeitherReservedConsumedNorDispatched(OperationGrantScope scope)
    {
        using var f = Fixture();
        var question = await Present(f);
        var grant = Grant(f, scope);
        f.Store.Change(s => s with { Grants = [grant] });
        var before = f.Store.Snapshot;
        var result = await Explain(f, question);
        result.Reason.Should().Be("clipboard-read-exact-grant-observed");
        result.Explanation!.ApplicableGrant.Should().Be(grant);
        f.Store.Snapshot.Should().BeSameAs(before);
        (await Explain(f, question, result.Explanation)).Explanation.Should().Be(result.Explanation);
        f.Store.Snapshot.Should().BeSameAs(before);
        var used = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, Token));
        used.Outcome.Should().Be(HostInteractionOutcome.Consumed);
        used.Grant!.UseCount.Should().Be(1);
        used.Admission!.Dispatched.Should().BeFalse();
    }

    [Theory]
    [InlineData("consumed")]
    [InlineData("revoked")]
    [InlineData("generation")]
    [InlineData("foreign-session")]
    [InlineData("other-once-proposal")]
    [InlineData("effect")]
    [InlineData("definition")]
    [InlineData("dependency")]
    [InlineData("content")]
    [InlineData("implementation")]
    [InlineData("identity")]
    public async Task RetainedRecordIsNotApplicabilityAndObservationNeverRevokesIt(string change)
    {
        using var f = Fixture();
        var question = await Present(f);
        var grant = Grant(f, OperationGrantScope.Session);
        grant = change switch
        {
            "consumed" => grant with { Status = OperationGrantStatus.Consumed },
            "revoked" => grant with { Status = OperationGrantStatus.Revoked, RevocationReason = "explicit-remove-or-edit" },
            "generation" => grant with { SessionGeneration = new(2) },
            "foreign-session" => grant with { ApprovedProposal = new(
                new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), RequestOrigin.LocalUi, new(Guid.NewGuid())),
                grant.ApprovedProposal.ProposalId, new(1),
                grant.ApprovedProposal.Binding, HostOperationEffect.BoundedRead, grant.ApprovedProposal.ExpiresAt) },
            "other-once-proposal" => grant with { Scope = OperationGrantScope.Once, ApprovedProposal = new(
                f.Request, new(Guid.NewGuid()), new(1), grant.ApprovedProposal.Binding,
                HostOperationEffect.BoundedRead, grant.ApprovedProposal.ExpiresAt) },
            "effect" => grant with { ApprovedProposal = new(f.Request, grant.ApprovedProposal.ProposalId,
                new(1), grant.ApprovedProposal.Binding, HostOperationEffect.BoundedWrite, grant.ApprovedProposal.ExpiresAt) },
            _ => grant with { ApprovedProposal = new(f.Request, grant.ApprovedProposal.ProposalId,
                new(1), ChangedBinding(change), HostOperationEffect.BoundedRead, grant.ApprovedProposal.ExpiresAt) },
        };
        f.Store.Change(s => s with { Grants = [grant] });
        var before = f.Store.Snapshot;
        var result = await Explain(f, question);
        result.Reason.Should().Be("clipboard-read-exact-grant-required");
        result.Explanation!.ApplicableGrant.Should().BeNull();
        f.Store.Snapshot.Should().BeSameAs(before);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("locked")]
    [InlineData("mandatory")]
    [InlineData("unknown-policy")]
    [InlineData("admitted")]
    [InlineData("ended")]
    [InlineData("no-proposal")]
    [InlineData("expired-proposal")]
    [InlineData("prohibited")]
    [InlineData("cancelled")]
    [InlineData("question-generation")]
    [InlineData("expected-generation")]
    [InlineData("expired-question")]
    [InlineData("expected-expiry")]
    [InlineData("expected-proposal")]
    [InlineData("current-proposal")]
    [InlineData("purpose")]
    [InlineData("source")]
    public async Task UnknownStaleOrClosedEvidenceCannotEstablishPolicy(string change)
    {
        using var f = Fixture();
        var question = await Present(f);
        var expected = change switch
        {
            "expected-generation" => question with { SessionGeneration = new(2) },
            "expected-expiry" => question with { ExpiresAt = question.ExpiresAt.AddMinutes(1) },
            "expected-proposal" => question with { Proposal = null },
            _ => question,
        };
        f.Store.Change(s => change switch
        {
            "absent" => s with { Questions = [] },
            "locked" => s with { Policy = s.Policy with { IsUnlocked = false } },
            "mandatory" => s with { Policy = s.Policy with { OtherMandatoryGatesSatisfied = false } },
            "unknown-policy" => s with { Permission = null },
            "admitted" => s with { Admissions = [new(Guid.NewGuid(), s.Proposal!, s.Permission!, SessionPermissionBasis.ApproveAll)] },
            "ended" => s with { Session = s.Session with { IsActive = false } },
            "no-proposal" => s with { Proposal = null },
            "expired-proposal" => s with { Proposal = new(f.Request, s.Proposal!.ProposalId, new(1),
                s.Proposal.Binding, HostOperationEffect.BoundedRead, f.Time.Now) },
            "prohibited" => s with { Proposal = new(f.Request, s.Proposal!.ProposalId, new(1),
                s.Proposal.Binding, HostOperationEffect.Prohibited, s.Proposal.ExpiresAt) },
            "cancelled" => s with { Questions = [question with { Status = QuestionStatus.Cancelled }] },
            "question-generation" => s with { Questions = [question with { SessionGeneration = new(2) }] },
            "expired-question" => s with { Questions = [question with { ExpiresAt = f.Time.Now }] },
            "current-proposal" => s with { Proposal = new(f.Request, new(Guid.NewGuid()), new(1),
                s.Proposal!.Binding, HostOperationEffect.BoundedRead, s.Proposal.ExpiresAt) },
            "purpose" => s with { Questions = [question with { Spec = Spec("clarification", "clipboard-read") }] },
            "source" => s with { Questions = [question with { Spec = Spec("authorization", "another-tool") }] },
            _ => s,
        });
        var before = f.Store.Snapshot;
        var result = await Explain(f, expected);
        result.Outcome.Should().Be(HostInteractionOutcome.Denied);
        result.Reason.Should().Be("cannot-establish-current-pending-clipboard-read");
        result.Explanation.Should().BeNull();
        result.Admission.Should().BeNull();
        f.Store.Snapshot.Should().BeSameAs(before);
        f.Store.Commits[^1].Audit.Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("effect")]
    [InlineData("system")]
    [InlineData("voice-grant")]
    public async Task OtherDescriptorsAndNonNativeGrantsCannotBecomeClipboardReadFacts(string change)
    {
        using var f = Fixture(change is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (change is "binding") { f.ChangeProposal(InteractionFixture.Binding()); }
        if (change is "effect") { f.ChangeProposal(effect: HostOperationEffect.BoundedWrite); }
        var question = await Present(f);
        if (change is "voice-grant")
        { f.Store.Change(s => s with { Grants = [Grant(f, OperationGrantScope.Once) with { CreatorChannel = RequestOrigin.ActivatedVoice }] }); }
        var result = await Explain(f, question);
        result.Outcome.Should().Be(HostInteractionOutcome.Denied);
        result.Explanation.Should().BeNull();
        result.Reason.Should().Be(change is "voice-grant" ? "clipboard-read-native-grant-required"
            : "cannot-establish-current-pending-clipboard-read");
        f.Store.Snapshot.Admissions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("level")]
    [InlineData("call")]
    [InlineData("grant-added")]
    [InlineData("grant-consumed")]
    [InlineData("grant-revoked")]
    public async Task ReturnRevalidatesSameFactsAndNeverFollowsChangedAuthority(string change)
    {
        using var f = Fixture();
        var question = await Present(f);
        if (change is "grant-consumed" or "grant-revoked")
        { f.Store.Change(s => s with { Grants = [Grant(f, OperationGrantScope.Once)] }); }
        var observed = (await Explain(f, question)).Explanation!;
        f.Store.Change(s => change switch
        {
            "level" => s with { Permission = s.Permission! with { Level = SessionPermissionLevel.ApproveAll, Revision = new(2) } },
            "call" => s with { Policy = s.Policy with { IsProtectedCall = true } },
            "grant-added" => s with { Grants = [Grant(f, OperationGrantScope.Perpetual)] },
            "grant-consumed" => s with { Grants = [s.Grants[0] with { Status = OperationGrantStatus.Consumed, Revision = new(2), UseCount = 1 }] },
            _ => s with { Grants = [s.Grants[0].Revoke("explicit-remove-or-edit")] },
        });
        var before = f.Store.Snapshot;
        var result = await Explain(f, question, observed);
        result.Outcome.Should().Be(HostInteractionOutcome.Conflict);
        result.Reason.Should().Be("pending-action-explanation-changed");
        result.Explanation.Should().BeNull();
        f.Store.Snapshot.Should().BeSameAs(before);
    }

    [Theory]
    [InlineData("storage")]
    [InlineData("audit")]
    [InlineData("cancel")]
    [InlineData("terminal")]
    public async Task FailedObservationDoesNotReturnSuccessOrChangeAuthority(string failure)
    {
        using var f = Fixture();
        var question = await Present(f);
        var before = f.Store.Snapshot;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        f.Store.FailStorage = failure is "storage";
        f.Store.FailAudit = failure is "audit";
        if (failure is "cancel") { f.Store.BeforeTransition = cancellation.Cancel; }
        if (failure is "terminal")
        { f.Store.Change(s => s with { Intent = new(f.Request, new(2), HostTaskState.Cancelled) }); }
        var run = () => f.RunAsync(() => f.Authorization.ExplainPendingClipboardReadAsync(question, null, cancellation.Token));
        if (failure is "cancel") { await run.Should().ThrowAsync<OperationCanceledException>(); }
        else if (failure is "terminal") { await run.Should().ThrowAsync<InvalidDataException>(); }
        else { await run.Should().ThrowAsync<IOException>(); }
        f.Store.Snapshot.Grants.Should().BeEmpty();
        f.Store.Snapshot.Admissions.Should().BeEmpty();
        f.Store.Snapshot.Questions.Should().Equal(before.Questions);
    }

    private static InteractionFixture Fixture(RequestOrigin origin = RequestOrigin.LocalUi)
    {
        var f = new InteractionFixture(origin);
        f.ChangeProposal(ClipboardPermissionSourceUsePolicy.CreateReadBinding());
        f.Store.Change(s => s with { Permission = SessionPermissionPolicy.Default(s.Session) });
        return f;
    }

    private static QuestionSpec Spec(string purpose = "authorization", string source = "clipboard-read") =>
        new("private-body-marker", QuestionKind.SingleChoice, [new("once", "Once")], purpose: purpose, sourceId: source);

    private static async Task<HostQuestionRecord> Present(InteractionFixture f) =>
        (await f.RunAsync(() => f.Authorization.RequestGrantAsync(f.Request, Spec(), Token))).Question!;

    private static Task<HostInteractionDecision> Explain(InteractionFixture f, HostQuestionRecord question,
        PendingOperationExplanation? previous = null) =>
        f.RunAsync(() => f.Authorization.ExplainPendingClipboardReadAsync(question, previous, Token));

    private static OperationGrant Grant(InteractionFixture f, OperationGrantScope scope) =>
        new(new(Guid.NewGuid()), new(1), f.Store.Snapshot.Proposal!, scope, f.Store.Snapshot.Session.Generation,
            RequestOrigin.LocalUi, f.Time.Now);

    private static ExactOperationBinding ChangedBinding(string field)
    {
        var b = ClipboardPermissionSourceUsePolicy.CreateReadBinding();
        var changed = new string('b', 64);
        return new(b.ActionId, b.SourcePartition, b.SkillId, field is "definition" ? changed : b.DefinitionDigest,
            field is "dependency" ? changed : b.DeclaredResourceDigest, field is "content" ? changed : b.TrackedContentDigest,
            field is "implementation" ? changed : b.ImplementationDigest, b.InvocationDigest, b.ResourceDigest,
            field is "identity" ? changed : b.IdentityDigest, b.DestinationDigest, b.TransformationDigest, b.PolicyRevision);
    }
}
