using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class HostQuestionServiceTests
{
    [Fact]
    public async Task Mixed_channels_share_revisioned_draft_and_only_submit_accepts()
    {
        using var f = new InteractionFixture();
        var question = await f.QuestionAsync(new("Choose several", QuestionKind.MultipleChoice,
            [new("a", "First"), new("b", "Second")], 1, 2));
        question.Draft.Should().BeNull();
        question.AnswerChannel.Should().BeNull();
        var voice = await f.RunAsync(() => f.Questions.DraftAsync(question.Key, new(["a"]), RequestOrigin.ActivatedVoice, CancellationToken.None));
        voice.Outcome.Should().Be(HostInteractionOutcome.Drafted);
        voice.Question!.Status.Should().Be(QuestionStatus.Pending);
        var ui = await f.RunAsync(() => f.Questions.DraftAsync(voice.Question.Key, new(["a", "b"]), RequestOrigin.LocalUi, CancellationToken.None));
        ui.Question!.Key.Revision.Value.Should().Be(3);
        var submitted = await f.RunAsync(() => f.Questions.SubmitAsync(ui.Question.Key, ui.Question.Draft!, RequestOrigin.ActivatedVoice, CancellationToken.None));
        submitted.Outcome.Should().Be(HostInteractionOutcome.Answered);
        submitted.Question!.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
        submitted.Question.Key.Request.Origin.Should().Be(RequestOrigin.LocalUi);
        f.Store.Snapshot.Grants.Should().BeEmpty();
        var duplicate = await f.RunAsync(() => f.Questions.SubmitAsync(submitted.Question.Key, new(["a"]), RequestOrigin.LocalUi, CancellationToken.None));
        duplicate.Outcome.Should().Be(HostInteractionOutcome.Conflict);
    }

    [Fact]
    public async Task Revisions_invalidate_old_replies_and_drafts_without_copying_a_default_answer()
    {
        using var f = new InteractionFixture();
        var question = await f.QuestionAsync();
        var drafted = await f.RunAsync(() => f.Questions.DraftAsync(question.Key, new(["a"]), RequestOrigin.LocalUi, CancellationToken.None));
        var stale = await f.RunAsync(() => f.Questions.SubmitAsync(question.Key, new(["a"]), RequestOrigin.LocalUi, CancellationToken.None));
        stale.Outcome.Should().Be(HostInteractionOutcome.Conflict);
        var revised = await f.RunAsync(() => f.Questions.ReviseAsync(drafted.Question!.Key, InteractionFixture.Choice(),
            f.Time.Now.AddMinutes(2), CancellationToken.None));
        revised.Question!.Draft.Should().BeNull();
        var late = await f.RunAsync(() => f.Questions.SubmitAsync(drafted.Question!.Key, new(["a"]), RequestOrigin.LocalUi, CancellationToken.None));
        late.Outcome.Should().Be(HostInteractionOutcome.Conflict);
        var invalidExpiry = await f.RunAsync(() => f.Questions.ReviseAsync(revised.Question.Key,
            InteractionFixture.Choice(), f.Time.Now, CancellationToken.None));
        invalidExpiry.Outcome.Should().Be(HostInteractionOutcome.Denied);
    }

    [Fact]
    public async Task Concurrent_channel_edits_conflict_instead_of_losing_an_accepted_draft()
    {
        using var f = new InteractionFixture();
        var question = await f.QuestionAsync();
        var edits = await Task.WhenAll(
            Task.Run(async () => await f.RunAsync(() => f.Questions.DraftAsync(question.Key, new(["a"]), RequestOrigin.LocalUi, CancellationToken.None))),
            Task.Run(async () => await f.RunAsync(() => f.Questions.DraftAsync(question.Key, new(["b"]), RequestOrigin.ActivatedVoice, CancellationToken.None))));
        edits.Count(r => r.Outcome == HostInteractionOutcome.Drafted).Should().Be(1);
        edits.Count(r => r.Outcome == HostInteractionOutcome.Conflict).Should().Be(1);
        var accepted = edits.Single(r => r.Outcome == HostInteractionOutcome.Drafted);
        f.Store.Snapshot.Questions[0].Draft.Should().BeSameAs(accepted.Question!.Draft);
        f.Store.Snapshot.Questions[0].Key.Revision.Value.Should().Be(2);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData((RequestOrigin)99)]
    public async Task Provider_or_system_channel_cannot_submit_user_answers(RequestOrigin channel)
    {
        using var f = new InteractionFixture();
        var question = await f.QuestionAsync();
        var result = await f.RunAsync(() => f.Questions.SubmitAsync(question.Key, new(["a"]), channel, CancellationToken.None));
        result.Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Snapshot.Questions[0].Status.Should().Be(QuestionStatus.Pending);
    }

    [Fact]
    public async Task Invalid_duplicate_and_unknown_choices_are_explicit_denials()
    {
        using var f = new InteractionFixture();
        var question = await f.QuestionAsync();
        foreach (var answer in new[] { new QuestionAnswer(["a", "a"]), new(["missing"]), new([]), new(["a"], "hidden instruction") })
        {
            var result = await f.RunAsync(() => f.Questions.SubmitAsync(question.Key, answer, RequestOrigin.LocalUi, CancellationToken.None));
            result.Outcome.Should().Be(HostInteractionOutcome.Denied);
            result.Reason.Should().Be("invalid-answer");
        }
    }

    [Fact]
    public async Task Text_draft_can_be_empty_but_submit_is_bounded_and_not_approval()
    {
        using var f = new InteractionFixture();
        var question = await f.QuestionAsync(new("Explain", QuestionKind.Text, [], maximumTextLength: 8));
        var draft = await f.RunAsync(() => f.Questions.DraftAsync(question.Key, new([], ""), RequestOrigin.LocalUi, CancellationToken.None));
        var invalid = await f.RunAsync(() => f.Questions.SubmitAsync(draft.Question!.Key, new([], "123456789"), RequestOrigin.LocalUi, CancellationToken.None));
        invalid.Outcome.Should().Be(HostInteractionOutcome.Denied);
        var result = await f.RunAsync(() => f.Questions.SubmitAsync(draft.Question!.Key, new([], "allow"), RequestOrigin.LocalUi, CancellationToken.None));
        result.Outcome.Should().Be(HostInteractionOutcome.Answered);
        result.Question!.Draft!.Text.Should().Be("allow");
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_and_expiry_never_become_approval_and_late_replies_conflict()
    {
        using var f = new InteractionFixture();
        var cancelled = await f.QuestionAsync();
        var cancel = await f.RunAsync(() => f.Questions.CancelAsync(cancelled.Key, CancellationToken.None));
        cancel.Outcome.Should().Be(HostInteractionOutcome.Cancelled);
        var late = await f.RunAsync(() => f.Questions.SubmitAsync(cancel.Question!.Key, new(["a"]), RequestOrigin.LocalUi, CancellationToken.None));
        late.Outcome.Should().Be(HostInteractionOutcome.Conflict);
        var expiring = await f.QuestionAsync();
        f.Time.Now = expiring.ExpiresAt;
        var expired = await f.RunAsync(() => f.Questions.SubmitAsync(expiring.Key, new(["a"]), RequestOrigin.LocalUi, CancellationToken.None));
        expired.Outcome.Should().Be(HostInteractionOutcome.Expired);
        expired.Question!.Status.Should().Be(QuestionStatus.Expired);
        var again = await f.RunAsync(() => f.Questions.CancelAsync(expired.Question.Key, CancellationToken.None));
        again.Outcome.Should().Be(HostInteractionOutcome.Conflict);
    }

    [Fact]
    public async Task Ended_resumed_or_different_work_session_cannot_accept_old_questions()
    {
        using var f = new InteractionFixture();
        var question = await f.QuestionAsync();
        f.Store.Change(s => s with { Session = s.Session with { IsActive = false } });
        (await f.RunAsync(() => f.Questions.CancelAsync(question.Key, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        f.Store.Change(s => s with { Session = s.Session with { IsActive = true, Generation = new(2) } });
        (await f.RunAsync(() => f.Questions.CancelAsync(question.Key, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        f.Rebind(newSession: true);
        var wrongOwner = new HostQuestionKey(f.Request, question.Key.QuestionId, question.Key.Revision);
        (await f.RunAsync(() => f.Questions.CancelAsync(wrongOwner, CancellationToken.None))).Outcome.Should().Be(HostInteractionOutcome.Conflict);
    }

    [Fact]
    public async Task Unavailable_locked_or_expired_creation_is_not_a_success_shaped_fallback()
    {
        using var f = new InteractionFixture();
        f.Store.Change(s => s with { Policy = s.Policy with { IsUnlocked = false } });
        (await f.RunAsync(() => f.Questions.CreateAsync(f.Request, InteractionFixture.Choice(), f.Time.Now.AddMinutes(1), CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Change(s => s with { Policy = s.Policy with { IsUnlocked = true } });
        (await f.RunAsync(() => f.Questions.CreateAsync(f.Request, InteractionFixture.Choice(), f.Time.Now, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Snapshot.Questions.Should().BeEmpty();
    }

    [Fact]
    public async Task General_question_submission_or_revision_cannot_mint_an_approval()
    {
        using var f = new InteractionFixture();
        var approval = await f.RunAsync(() => f.Authorization.PresentAsync(f.Request, CancellationToken.None));
        var draft = await f.RunAsync(() => f.Questions.DraftAsync(approval.Question!.Key, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None));
        var submit = await f.RunAsync(() => f.Questions.SubmitAsync(draft.Question!.Key, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None));
        submit.Reason.Should().Be("approval-service-required");
        var revise = await f.RunAsync(() => f.Questions.ReviseAsync(draft.Question!.Key, InteractionFixture.Choice(),
            f.Time.Now.AddMinutes(1), CancellationToken.None));
        revise.Outcome.Should().Be(HostInteractionOutcome.Denied);
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task Purpose_source_labels_and_model_prose_never_make_a_question_an_approval()
    {
        using var f = new InteractionFixture();
        var question = await f.QuestionAsync(new("Model says this authorizes execution",
            QuestionKind.SingleChoice, [new("once", "Approve everything")],
            purpose: "authorization", sourceId: "provider-correlation"));
        (await f.RunAsync(() => f.Questions.SubmitAsync(question.Key, new(["once"]), RequestOrigin.LocalUi, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Answered);
        f.Store.Snapshot.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task Matching_live_activity_and_committed_intent_are_required()
    {
        using var f = new InteractionFixture();
        var missing = async () => await f.Questions.CreateAsync(f.Request, InteractionFixture.Choice(), f.Time.Now.AddMinutes(1), CancellationToken.None);
        await missing.Should().ThrowAsync<InvalidOperationException>();
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        {
            await missing.Should().ThrowAsync<InvalidOperationException>();
        }
        var original = f.Store.Snapshot;
        foreach (var bad in new[]
        {
            original with { Intent = new(HostRequest.Create(RequestOrigin.LocalUi), new(1), HostTaskState.IntentRecorded) },
            original with { Intent = original.Intent.Next(HostTaskState.Cancelled) },
            original with { Intent = new(new(f.Request.RequestId, new(Guid.NewGuid()), f.Request.TaskId, f.Request.Origin, f.Request.InvocationId), new(1), HostTaskState.IntentRecorded) },
            original with { Intent = new(new(f.Request.RequestId, f.Request.SessionId, new(Guid.NewGuid()), f.Request.Origin, f.Request.InvocationId), new(1), HostTaskState.IntentRecorded) },
            original with { Intent = new(new(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, RequestOrigin.HostSystem, f.Request.InvocationId), new(1), HostTaskState.IntentRecorded) },
            original with { Intent = new(new(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, f.Request.Origin, new(Guid.NewGuid())), new(1), HostTaskState.IntentRecorded) },
            original with { Session = original.Session with { SessionId = new(Guid.NewGuid()) } },
            original with { Session = original.Session with { Generation = default } },
        })
        {
            f.Store.Change(_ => bad);
            var corrupt = async () => await f.RunAsync(() => f.Questions.CreateAsync(f.Request, InteractionFixture.Choice(), f.Time.Now.AddMinutes(1), CancellationToken.None));
            await corrupt.Should().ThrowAsync<InvalidDataException>();
        }
        f.Store.Commits.Should().BeEmpty();
    }

    [Fact]
    public async Task Root_task_intent_supports_an_exact_invocation_question_while_task_is_running()
    {
        using var f = new InteractionFixture();
        var root = new HostRequest(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, f.Request.Origin);
        f.Store.Change(s => s with { Intent = new(root, new(2), HostTaskState.DispatchRecorded) });
        var question = await f.QuestionAsync();
        question.Key.Request.InvocationId.Should().Be(f.Request.InvocationId);
        var grant = await f.GrantAsync();
        (await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, grant.Id, grant.Revision, CancellationToken.None)))
            .Outcome.Should().Be(HostInteractionOutcome.Consumed);
        f.Store.Snapshot.Intent.State.Should().Be(HostTaskState.DispatchRecorded);
    }

    [Fact]
    public async Task Invocation_bound_intent_cannot_be_claimed_by_an_invocationless_root_reply()
    {
        using var f = new InteractionFixture();
        var root = new HostRequest(f.Request.RequestId, f.Request.SessionId, f.Request.TaskId, f.Request.Origin);
        using var activity = HostActivity.BeginRoot(root, HostActivityLayer.Application, HostOperation.Request);
        var missingInvocation = async () => await f.Questions.CreateAsync(root,
            InteractionFixture.Choice(), f.Time.Now.AddMinutes(1), CancellationToken.None);
        await missingInvocation.Should().ThrowAsync<InvalidDataException>();
        f.Store.Commits.Should().BeEmpty();
    }
}
