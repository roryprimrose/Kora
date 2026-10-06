using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class InteractionCommitTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Unavailable_storage_or_audit_rolls_back_question_and_grant(bool storage, bool audit)
    {
        using var f = new InteractionFixture();
        var presented = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        var prior = f.Store.Snapshot;
        f.Store.FailStorage = storage;
        f.Store.FailAudit = audit;
        var approve = async () => await f.RunAsync(() => f.Authorization.ApproveAsync(presented.Question!.Key, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None));
        await approve.Should().ThrowAsync<IOException>();
        f.Store.Snapshot.Should().BeSameAs(prior);
        f.Store.Commits.Should().ContainSingle();
        f.Store.LastActivity!.Outcome.Should().Be(HostOperationOutcome.Failed);
        f.Store.FailAudit = false;
        f.Store.FailStorage = false;
        var grant = await f.GrantAfterPresentationAsync(presented.Question!);
        prior = f.Store.Snapshot;
        f.Store.FailAudit = true;
        var consume = async () => await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None));
        await consume.Should().ThrowAsync<IOException>();
        f.Store.Snapshot.Should().BeSameAs(prior);
        f.Store.Snapshot.Grants[0].UseCount.Should().Be(0);
    }

    [Fact]
    public async Task Cancellation_before_or_during_commit_certifies_no_authority_or_answer()
    {
        using var f = new InteractionFixture();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var create = async () => await f.RunAsync(() => f.Questions.CreateAsync(f.Request, InteractionFixture.Choice(), f.Time.Now.AddMinutes(1), cancelled.Token));
        await create.Should().ThrowAsync<OperationCanceledException>();
        f.Store.Commits.Should().BeEmpty();
        var presented = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        using var during = new CancellationTokenSource();
        f.Store.BeforeCommit = during.Cancel;
        var approve = async () => await f.RunAsync(() => f.Authorization.ApproveAsync(presented.Question!.Key, new(["once"]), RequestOrigin.LocalUi, during.Token));
        await approve.Should().ThrowAsync<OperationCanceledException>();
        f.Store.Snapshot.Grants.Should().BeEmpty();
        f.Store.Snapshot.Questions[0].Status.Should().Be(QuestionStatus.Pending);
        f.Store.LastActivity!.Outcome.Should().Be(HostOperationOutcome.Cancelled);
    }

    [Fact]
    public async Task Uncertain_commit_throws_instead_of_success_or_false_cancellation_and_never_replays_use()
    {
        using var f = new InteractionFixture();
        var grant = await f.GrantAsync();
        f.Store.UncertainCommit = true;
        var consume = async () => await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None));
        await consume.Should().ThrowAsync<IOException>();
        f.Store.Snapshot.Grants[0].UseCount.Should().Be(1);
        f.Store.UncertainCommit = false;
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Conflict);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    [InlineData(RequestOrigin.HostSystem, SecurityAuditInitiator.System)]
    public async Task Audit_is_typed_correlated_content_minimizing_and_preserves_initiator(
        RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        using var f = new InteractionFixture(origin);
        var result = await f.RunAsync(() => f.Questions.CreateAsync(f.Request,
            new("PRIVATE TEXT NOT IN AUDIT", QuestionKind.Text, [], maximumTextLength: 4), f.Time.Now.AddMinutes(1), CancellationToken.None));
        var commit = f.Store.Commits.Single();
        commit.Audit.Category.Should().Be(SecurityAuditCategory.SecurityApproval);
        commit.Audit.Initiator.Should().Be(initiator);
        commit.Audit.Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
        commit.Audit.ApprovalId.Should().Be(result.Question!.Key.QuestionId.Value);
        commit.Audit.TargetId.Should().Be(f.Request.TaskId.Value.ToString("D"));
        commit.Audit.ActionId.Should().Be("question.create");
        commit.Audit.ReasonCode.Should().Be("question-presented");
        f.Store.LastActivity!.CorrelationId.Should().Be(commit.Audit.CorrelationId);
        f.Store.LastActivity.Activity!.TraceId.Should().NotBe(default(ActivityTraceId));
        f.Store.LastActivity.Activity.TagObjects.Should().NotContain(t => t.Key.Contains("text", StringComparison.Ordinal));
        f.Store.LastActivity.Outcome.Should().Be(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task Denial_and_cancel_are_truthful_audit_outcomes_not_model_authority()
    {
        using var f = new InteractionFixture();
        var question = await f.QuestionAsync();
        await f.RunAsync(() => f.Questions.SubmitAsync(question.Key, new(["provider-id"]), RequestOrigin.LocalUi, CancellationToken.None));
        f.Store.Commits[^1].Audit.Outcome.Should().Be(SecurityAuditOutcome.Denied);
        f.Store.LastActivity!.Outcome.Should().Be(HostOperationOutcome.Failed);
        await f.RunAsync(() => f.Questions.CancelAsync(question.Key, CancellationToken.None));
        f.Store.Commits[^1].Audit.Outcome.Should().Be(SecurityAuditOutcome.Cancelled);
    }
}
