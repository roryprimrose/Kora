using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class HostQuestionReviewServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_review_is_audited_without_answer_grant_or_revision_mutation(bool approval)
    {
        using var f = new InteractionFixture();
        var question = approval
            ? (await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None))).Question!
            : await f.QuestionAsync();
        var before = f.Store.Snapshot;
        var service = new HostQuestionReviewService(f.Store, f.Time);
        var result = await f.RunAsync(() => service.ReviewAsync(question.Key, CancellationToken.None));
        result.Outcome.Should().Be(HostInteractionOutcome.Presented);
        result.Question.Should().BeSameAs(question);
        result.Reason.Should().Be("exact-question-reviewed");
        f.Store.Snapshot.Should().BeSameAs(before);
        f.Store.Commits.Last().Audit.ActionId.Should().Be("question.review");
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("inactive")]
    [InlineData("revision")]
    [InlineData("missing")]
    [InlineData("closed")]
    [InlineData("generation")]
    public async Task Unavailable_or_stale_question_never_acquires_review_authority(string change)
    {
        using var f = new InteractionFixture();
        var question = await f.QuestionAsync();
        var key = question.Key;
        switch (change)
        {
            case "locked": f.Store.Change(s => s with { Policy = s.Policy with { IsUnlocked = false } }); break;
            case "inactive": f.Store.Change(s => s with { Session = s.Session with { IsActive = false } }); break;
            case "revision": key = key.Next(); break;
            case "missing": key = new(f.Request, new(Guid.NewGuid()), new(1)); break;
            case "closed": f.Store.Change(s => s with { Questions = [question with { Status = QuestionStatus.Cancelled }] }); break;
            case "generation": f.Store.Change(s => s with { Session = s.Session with { Generation = new(2) } }); break;
        }
        var service = new HostQuestionReviewService(f.Store, f.Time);
        var result = await f.RunAsync(() => service.ReviewAsync(key, CancellationToken.None));
        result.Outcome.Should().Be(HostInteractionOutcome.Conflict);
        result.Question.Should().BeNull();
    }

    [Fact]
    public async Task Expiry_is_committed_instead_of_reviewed()
    {
        using var f = new InteractionFixture();
        var question = await f.QuestionAsync();
        f.Time.Now = question.ExpiresAt;
        var result = await f.RunAsync(() => new HostQuestionReviewService(f.Store, f.Time).ReviewAsync(question.Key, CancellationToken.None));
        result.Outcome.Should().Be(HostInteractionOutcome.Expired);
        result.Question!.Status.Should().Be(QuestionStatus.Expired);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("revised")]
    [InlineData("mandatory-gate")]
    [InlineData("foreign-request")]
    public async Task Operation_review_requires_the_exact_current_host_proposal_and_mandatory_gates(string change)
    {
        using var f = new InteractionFixture();
        var question = (await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None))).Question!;
        if (string.Equals(change, "absent", StringComparison.Ordinal)) { f.Store.Change(s => s with { Proposal = null }); }
        if (string.Equals(change, "revised", StringComparison.Ordinal)) { f.ChangeProposal(revision: new(2)); }
        if (string.Equals(change, "mandatory-gate", StringComparison.Ordinal)) { f.Store.Change(s => s with { Policy = s.Policy with { OtherMandatoryGatesSatisfied = false } }); }
        if (string.Equals(change, "foreign-request", StringComparison.Ordinal))
        {
            var proposal = question.Proposal!;
            var foreign = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
                RequestOrigin.LocalUi, proposal.Request.InvocationId);
            var replacement = new Kora.Core.Authorization.HostOperationProposal(foreign, proposal.ProposalId,
                proposal.Revision, proposal.Binding, proposal.Effect, proposal.ExpiresAt);
            f.Store.Change(s => s with { Proposal = replacement, Questions = [question with { Proposal = replacement }] });
        }
        var result = await f.RunAsync(() => new HostQuestionReviewService(f.Store, f.Time).ReviewAsync(question.Key, CancellationToken.None));
        result.Outcome.Should().Be(HostInteractionOutcome.Denied);
        result.Reason.Should().Be("review-not-applicable");
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task Review_audit_failure_cannot_return_content_or_success()
    {
        using var f = new InteractionFixture();
        var question = await f.QuestionAsync();
        f.Store.FailAudit = true;
        var read = () => f.RunAsync(() => new HostQuestionReviewService(f.Store, f.Time).ReviewAsync(question.Key, CancellationToken.None));
        await read.Should().ThrowAsync<IOException>();
        f.Store.Commits.Should().ContainSingle();
    }
}
