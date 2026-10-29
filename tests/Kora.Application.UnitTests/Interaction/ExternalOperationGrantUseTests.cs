using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class ExternalOperationGrantUseTests
{
    [Theory]
    [InlineData("current")]
    [InlineData("question")]
    [InlineData("once")]
    [InlineData("admission")]
    [InlineData("proposal")]
    [InlineData("id")]
    [InlineData("revision")]
    [InlineData("missing-id")]
    [InlineData("missing-revision")]
    [InlineData("revoked")]
    public async Task SavedExternalAuthorityRequiresTheExactCurrentCommittedAdmission(string fault)
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync("perpetual");
        var consumed = await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, Token));
        var proposal = f.Store.Snapshot.Proposal!;
        var presented = fault is "question" ? f.Store.Snapshot.Questions.Single() : null;
        if (fault is "once") { consumed = consumed with { Grant = consumed.Grant! with { Scope = OperationGrantScope.Once } }; }
        if (fault is "admission") { consumed = consumed with { Admission = null }; }
        if (fault is "proposal") { consumed = consumed with { Admission = consumed.Admission! with
        { Proposal = new(proposal.Request, new(Guid.NewGuid()), proposal.Revision, proposal.Binding, proposal.Effect, proposal.ExpiresAt) } }; }
        if (fault is "id") { consumed = consumed with { Admission = consumed.Admission! with { GrantId = new(Guid.NewGuid()) } }; }
        if (fault is "revision") { consumed = consumed with { Admission = consumed.Admission! with { GrantRevision = new(99) } }; }
        if (fault is "missing-id") { consumed = consumed with { Admission = consumed.Admission! with { GrantId = null } }; }
        if (fault is "missing-revision") { consumed = consumed with { Admission = consumed.Admission! with { GrantRevision = null } }; }
        if (fault is "revoked") { f.Store.Change(snapshot => snapshot with { Grants = [snapshot.Grants[0].Revoke("explicit-user-revocation")] }); }
        var result = await f.RunAsync(() => ExternalOperationGrantUse.ConsumeAsync(f.Authorization, proposal,
            f.Store.Snapshot.Session.Generation, presented, consumed, requireLocalApproval: true, Token));
        result.Outcome.Should().Be(fault is "current" ? HostInteractionOutcome.Consumed : HostInteractionOutcome.Denied);
        f.Store.Snapshot.Grants.Single().UseCount.Should().Be(1);
        f.Store.Snapshot.Admissions.Should().OnlyContain(admission => !admission.Dispatched);
    }

    [Theory]
    [InlineData("once", false)]
    [InlineData("session", false)]
    [InlineData("perpetual", false)]
    [InlineData("once", true)]
    public async Task FreshExternalConsentAcceptsOnlyMatchingDurationAndRequiredLocalChannel(string scope, bool voice)
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync(scope, voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        var answer = f.Store.Snapshot.Questions.Single();
        var decision = new HostInteractionDecision(HostInteractionOutcome.Approved, "approval-recorded", answer, grant);
        var result = await f.RunAsync(() => ExternalOperationGrantUse.ConsumeAsync(f.Authorization,
            f.Store.Snapshot.Proposal!, f.Store.Snapshot.Session.Generation, answer, decision,
            requireLocalApproval: true, Token));
        result.Outcome.Should().Be(voice ? HostInteractionOutcome.Denied : HostInteractionOutcome.Consumed);
        f.Store.Snapshot.Grants.Single().UseCount.Should().Be(voice ? 0 : 1);
    }

    [Fact]
    public async Task UnknownExternalGrantScopeIsExplicitlyRejected()
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync();
        var answer = f.Store.Snapshot.Questions.Single();
        var decision = new HostInteractionDecision(HostInteractionOutcome.Approved, "approval-recorded",
            answer, grant with { Scope = (OperationGrantScope)99 });
        var use = () => f.RunAsync(() => ExternalOperationGrantUse.ConsumeAsync(f.Authorization,
            f.Store.Snapshot.Proposal!, f.Store.Snapshot.Session.Generation, answer, decision, false, Token));
        await use.Should().ThrowAsync<InvalidDataException>();
        f.Store.Snapshot.Admissions.Should().BeEmpty();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FreshConsentWithoutItsPresentedQuestionCannotDispatch()
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync();
        var decision = new HostInteractionDecision(HostInteractionOutcome.Approved, "approval-recorded",
            f.Store.Snapshot.Questions.Single(), grant);
        var result = await f.RunAsync(() => ExternalOperationGrantUse.ConsumeAsync(f.Authorization,
            f.Store.Snapshot.Proposal!, f.Store.Snapshot.Session.Generation, null, decision, false, Token));
        result.Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Snapshot.Admissions.Should().BeEmpty();
    }
}
