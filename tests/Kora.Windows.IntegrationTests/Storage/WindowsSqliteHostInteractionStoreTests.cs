using System.Security.AccessControl;
using System.Security.Principal;

using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteHostInteractionStoreTests
{
    [WindowsFact]
    public async Task Typed_questions_options_drafts_answers_and_exact_grants_survive_reopen_without_replay_authority()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var spec = new QuestionSpec("Choose two", QuestionKind.MultipleChoice,
            [new("a", "First"), new("b", "Second")], minimum: 1, maximum: 2, purpose: "clarification", sourceId: "local");
        var presented = await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request, spec,
            fixture.Time.Now.AddMinutes(5), fixture.Token));
        var drafted = await fixture.RunAsync(() => fixture.Questions.DraftAsync(presented.Question!.Key, new(["a"]),
            RequestOrigin.ActivatedVoice, fixture.Token));
        var pendingApproval = await fixture.PresentAsync();
        var approved = await fixture.RunAsync(() => fixture.Authorization.ApproveAsync(pendingApproval.Key, new(["once"]),
            RequestOrigin.LocalUi, fixture.Token));
        fixture.Reopen();
        (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.Generation.Value.Should().Be(1);
        var questions = await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token);
        var draft = questions.Single(q => q.Key.QuestionId == presented.Question!.Key.QuestionId);
        draft.Spec.Options.Select(o => o.Id).Should().Equal("a", "b");
        draft.Draft!.Choices.Should().Equal("a");
        draft.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
        draft.Key.Should().Be(drafted.Question!.Key);
        var grants = await fixture.Store.ReadGrantsAsync(fixture.Token);
        grants.Should().ContainSingle().Which.Should().Be(approved.Grant);
        (await fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request,
            approved.Grant!.Id, approved.Grant.Revision, fixture.Token))).Outcome.Should().Be(HostInteractionOutcome.Denied);
        await fixture.PublishAsync();
        var answered = await fixture.RunAsync(() => fixture.Questions.SubmitAsync(draft.Key, new(["a", "b"]),
            RequestOrigin.LocalUi, fixture.Token));
        answered.Outcome.Should().Be(HostInteractionOutcome.Answered);
        var consumed = await fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request,
            approved.Grant!.Id, approved.Grant.Revision, fixture.Token));
        consumed.Outcome.Should().Be(HostInteractionOutcome.Consumed);
        fixture.Reopen();
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Single().Status.Should().Be(OperationGrantStatus.Consumed);
        (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token))
            .Single(q => q.Key.QuestionId == draft.Key.QuestionId).Draft!.Choices.Should().Equal("a", "b");
        (await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token))!.State.Should().Be(HostTaskState.IntentRecorded);
    }

    [WindowsFact]
    public async Task Bounded_text_answer_and_revision_conflict_do_not_create_authority()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var result = await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            new("Name", QuestionKind.Text, [], maximumTextLength: 32), fixture.Time.Now.AddMinutes(5), fixture.Token));
        var draft = await fixture.RunAsync(() => fixture.Questions.DraftAsync(result.Question!.Key,
            new([], "local answer"), RequestOrigin.LocalUi, fixture.Token));
        (await fixture.RunAsync(() => fixture.Questions.SubmitAsync(result.Question!.Key,
            new([], "stale"), RequestOrigin.LocalUi, fixture.Token))).Outcome.Should().Be(HostInteractionOutcome.Conflict);
        (await fixture.RunAsync(() => fixture.Questions.SubmitAsync(draft.Question!.Key,
            new([], "exactly answered"), RequestOrigin.ActivatedVoice, fixture.Token))).Outcome.Should().Be(HostInteractionOutcome.Answered);
        fixture.Reopen();
        (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single().Draft!.Text.Should().Be("exactly answered");
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Concurrent_approval_and_single_use_consume_commit_exactly_one_winner_each()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await fixture.PresentAsync();
        (await fixture.RunAsync(() => fixture.Authorization.PresentAsync(fixture.Request, fixture.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        var approvals = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.RunAsync(() =>
            fixture.Authorization.ApproveAsync(question.Key, new(["once"]), RequestOrigin.LocalUi, fixture.Token))));
        approvals.Count(a => a.Outcome == HostInteractionOutcome.Approved).Should().Be(1);
        var grant = approvals.Single(a => a.Grant is not null).Grant!;
        var uses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.RunAsync(() =>
            fixture.Authorization.ConsumeAsync(fixture.Request, grant.Id, grant.Revision, fixture.Token))));
        uses.Count(a => a.Outcome == HostInteractionOutcome.Consumed).Should().Be(1);
        var stored = (await fixture.Store.ReadGrantsAsync(fixture.Token)).Single();
        stored.UseCount.Should().Be(1);
        stored.Revision.Value.Should().Be(2);
        stored.Status.Should().Be(OperationGrantStatus.Consumed);
        fixture.Count("security_audit_events").Should().Be(20);
    }

    [WindowsFact]
    public async Task Done_resume_and_removal_invalidate_scoped_authority_but_not_independent_perpetual_records()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var sessionGrant = await fixture.GrantAsync("session");
        fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        await fixture.AdmitAsync(newSession: false);
        var once = await fixture.GrantAsync("once");
        fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        await fixture.AdmitAsync(newSession: false);
        var perpetual = await fixture.GrantAsync("perpetual");
        fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        await fixture.AdmitAsync(newSession: false);
        var pending = await fixture.PresentAsync();
        var done = await fixture.RunAsync(() => fixture.Store.SetSessionLifecycleAsync(fixture.Request, new(1),
            active: false, remove: false, fixture.Token));
        done.Generation.Value.Should().Be(2);
        done.IsActive.Should().BeFalse();
        fixture.Reopen();
        (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token)).Should().Be(done);
        var resumed = await fixture.RunAsync(() => fixture.Store.SetSessionLifecycleAsync(fixture.Request, new(2),
            active: true, remove: false, fixture.Token));
        resumed.Generation.Value.Should().Be(3);
        await fixture.PublishAsyncAfterLifecycle();
        (await fixture.RunAsync(() => fixture.Authorization.ApproveAsync(pending.Key, new(["once"]), RequestOrigin.LocalUi, fixture.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        var grants = await fixture.Store.ReadGrantsAsync(fixture.Token);
        grants.Single(g => g.Id == sessionGrant.Id).Status.Should().Be(OperationGrantStatus.Revoked);
        grants.Single(g => g.Id == once.Id).Status.Should().Be(OperationGrantStatus.Revoked);
        grants.Single(g => g.Id == perpetual.Id).Should().Be(perpetual);
        await fixture.RunAsync(() => fixture.Store.SetSessionLifecycleAsync(fixture.Request, new(3), active: false, remove: true, fixture.Token));
        (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Should().BeEmpty();
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle().Which.Should().Be(perpetual);
        var removedSession = fixture.Request.SessionId;
        fixture.Request = InteractionStorageFixture.NewRequest();
        await fixture.AdmitAsync(newSession: true);
        (await fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request, perpetual.Id, perpetual.Revision, fixture.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Consumed);
        fixture.Time.Now = fixture.Time.Now.AddYears(10);
        fixture.Reopen();
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle().Which.Scope.Should().Be(OperationGrantScope.Perpetual);
        (await fixture.Store.ReadSessionAsync(removedSession, fixture.Token))!.Generation.Value.Should().Be(4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task Changed_exact_binding_never_uses_stale_grant_and_observed_content_revocation_cannot_be_undone(int field)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var grant = await fixture.GrantAsync("perpetual");
        fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        await fixture.AdmitAsync(newSession: false);
        fixture.Proposal = new(fixture.Request, fixture.Proposal.ProposalId, new(2), InteractionStorageFixture.Binding(field),
            fixture.Proposal.Effect, fixture.Proposal.ExpiresAt);
        await fixture.PublishAsync();
        var use = await fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request, grant.Id, grant.Revision, fixture.Token));
        use.Outcome.Should().NotBe(HostInteractionOutcome.Consumed);
        if (field < 4)
        {
            (await fixture.Store.ReadGrantsAsync(fixture.Token)).Single().Status.Should().Be(OperationGrantStatus.Revoked);
            fixture.Proposal = new(fixture.Request, fixture.Proposal.ProposalId, new(3), InteractionStorageFixture.Binding(),
                fixture.Proposal.Effect, fixture.Proposal.ExpiresAt);
            await fixture.PublishAsync();
            (await fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request, grant.Id, new(2), fixture.Token)))
                .Outcome.Should().Be(HostInteractionOutcome.Conflict);
        }
    }

    [WindowsFact]
    public async Task Source_partition_proposal_revision_policy_lock_and_expiry_are_current_host_gates()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var pending = await fixture.PresentAsync();
        fixture.Proposal = new(fixture.Request, fixture.Proposal.ProposalId, new(2), InteractionStorageFixture.Binding(source: "other"),
            fixture.Proposal.Effect, fixture.Proposal.ExpiresAt);
        await fixture.PublishAsync();
        (await fixture.RunAsync(() => fixture.Authorization.ApproveAsync(pending.Key, new(["once"]), RequestOrigin.LocalUi, fixture.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
        var stalePublish = () => fixture.RunAsync(() => fixture.Store.PublishTrustedSnapshotAsync(fixture.Request,
            fixture.Policy, fixture.Proposal, 1, fixture.Token));
        await stalePublish.Should().ThrowAsync<InvalidOperationException>();
        fixture.Policy = fixture.Policy with { IsUnlocked = false };
        await fixture.PublishAsync();
        (await fixture.RunAsync(() => fixture.Authorization.PresentAsync(fixture.Request, fixture.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
        fixture.Policy = fixture.Policy with { IsUnlocked = true };
        await fixture.PublishAsync();
        fixture.Time.Now = fixture.Time.Now.AddHours(2);
        (await fixture.RunAsync(() => fixture.Authorization.ApproveAsync(pending.Key, new(["once"]), RequestOrigin.LocalUi, fixture.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Expired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revoke_edit_and_lifecycle_serialize_against_consumption(bool lifecycle)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var grant = await fixture.GrantAsync("once");
        var use = fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request, grant.Id, grant.Revision, fixture.Token));
        if (lifecycle)
        {
            var done = fixture.RunAsync(() => fixture.Store.SetSessionLifecycleAsync(fixture.Request, new(1), false, false, fixture.Token));
            await Task.WhenAll(use, done);
        }
        else
        {
            var revoke = fixture.RunAsync(() => fixture.Authorization.RevokeAsync(fixture.Request, grant.Id, grant.Revision, fixture.Token));
            await Task.WhenAll(use, revoke);
        }
        var stored = (await fixture.Store.ReadGrantsAsync(fixture.Token)).Single();
        stored.Status.Should().BeOneOf(OperationGrantStatus.Consumed, OperationGrantStatus.Revoked);
        stored.UseCount.Should().Be(use.Result.Outcome == HostInteractionOutcome.Consumed ? 1 : 0);
        (await fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request, grant.Id, stored.Revision, fixture.Token)))
            .Outcome.Should().NotBe(HostInteractionOutcome.Consumed);
    }

    [WindowsFact]
    public async Task Cancellation_and_wrong_intent_identity_cannot_admit_late_decisions()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var pending = await fixture.PresentAsync();
        var original = fixture.Request;
        fixture.Request = new(original.RequestId, original.SessionId, original.TaskId, RequestOrigin.ActivatedVoice, original.InvocationId);
        var hostile = () => fixture.RunAsync(() => fixture.Authorization.PresentAsync(fixture.Request, fixture.Token));
        await hostile.Should().ThrowAsync<InvalidDataException>();
        fixture.Request = original;
        await fixture.RunAsync(async () =>
        {
            var intent = (await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token))!;
            await fixture.Tasks.CommitAsync(intent.Next(HostTaskState.Cancelled), 1, fixture.Token);
        });
        var approve = () => fixture.RunAsync(() => fixture.Authorization.ApproveAsync(pending.Key, new(["once"]),
            RequestOrigin.LocalUi, fixture.Token));
        await approve.Should().ThrowAsync<InvalidDataException>();
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Cancelled_transition_rolls_back_question_grant_and_audit_together()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var pending = await fixture.PresentAsync();
        var count = fixture.Count("security_audit_events");
        using var cancellation = new CancellationTokenSource();
        var operation = () => fixture.RunAsync(() => fixture.Store.TransactAsync(fixture.Request, _ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }, cancellation.Token));
        await operation.Should().ThrowAsync<OperationCanceledException>();
        fixture.Count("security_audit_events").Should().Be(count);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().BeEmpty();
        (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single().Key.Should().Be(pending.Key);
    }

    [WindowsFact]
    public async Task Actual_audit_insert_failure_rolls_back_without_a_success_shaped_fallback()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await fixture.PresentAsync();
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.PublishAsync();
        var count = fixture.Count("security_audit_events");
        checkpoint.Audit = (connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "CREATE TRIGGER deny_audit BEFORE INSERT ON security_audit_events BEGIN SELECT RAISE(ABORT,'synthetic audit failure'); END;";
            command.ExecuteNonQuery();
        };
        var approve = () => fixture.RunAsync(() => fixture.Authorization.ApproveAsync(question.Key, new(["once"]),
            RequestOrigin.LocalUi, fixture.Token));
        await approve.Should().ThrowAsync<IOException>();
        fixture.Count("security_audit_events").Should().Be(count);
        fixture.Count("scoped_grants").Should().Be(0);
        checkpoint.Audit = null;
        (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single().Key.Should().Be(question.Key);
    }

    [WindowsFact]
    public async Task Cancellation_after_all_authority_writes_certifies_atomic_rollback()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await fixture.PresentAsync();
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.PublishAsync();
        var count = fixture.Count("security_audit_events");
        using var cancellation = new CancellationTokenSource();
        checkpoint.Commit = (_, _) => cancellation.Cancel();
        var approve = () => fixture.RunAsync(() => fixture.Authorization.ApproveAsync(question.Key, new(["once"]),
            RequestOrigin.LocalUi, cancellation.Token));
        await approve.Should().ThrowAsync<OperationCanceledException>();
        fixture.Count("security_audit_events").Should().Be(count);
        fixture.Count("scoped_grants").Should().Be(0);
        (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single().Key.Should().Be(question.Key);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Task_cancellation_waits_for_the_decision_commit_and_blocks_every_subsequent_decision(bool consuming)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await fixture.PresentAsync();
        OperationGrant? priorGrant = null;
        if (consuming)
        {
            priorGrant = (await fixture.RunAsync(() => fixture.Authorization.ApproveAsync(question.Key, new(["once"]),
                RequestOrigin.LocalUi, fixture.Token))).Grant!;
        }
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        await fixture.PublishAsync();
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        checkpoint.Commit = (_, _) =>
        {
            reached.Set();
            if (!release.Wait(TimeSpan.FromSeconds(3), fixture.Token))
            {
                throw new TimeoutException("The fixture failed to release its owned decision transaction.");
            }
        };
        var approval = consuming
            ? fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request, priorGrant!.Id, priorGrant.Revision, fixture.Token))
            : fixture.RunAsync(() => fixture.Authorization.ApproveAsync(question.Key, new(["once"]), RequestOrigin.LocalUi, fixture.Token));
        reached.Wait(TimeSpan.FromSeconds(3), fixture.Token).Should().BeTrue();
        var cancellation = fixture.RunAsync(async () =>
        {
            await fixture.Tasks.CommitAsync(new(fixture.Request, new(2), HostTaskState.Cancelled), 1, fixture.Token);
        });
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), fixture.Token);
            cancellation.IsCompleted.Should().BeFalse("the task partition lease is held through the authority commit");
        }
        finally
        {
            release.Set();
        }
        var result = await approval;
        await cancellation;
        result.Outcome.Should().Be(consuming ? HostInteractionOutcome.Consumed : HostInteractionOutcome.Approved);
        var consume = () => fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request,
            result.Grant!.Id, result.Grant.Revision, fixture.Token));
        await consume.Should().ThrowAsync<InvalidDataException>();
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Single().UseCount.Should().Be(consuming ? 1 : 0);
    }

    [WindowsFact]
    public async Task Active_session_restart_keeps_generation_but_recovered_task_cannot_restore_once_authority()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var sessionGrant = await fixture.GrantAsync("session");
        fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        await fixture.AdmitAsync(newSession: false);
        var once = await fixture.GrantAsync("once");
        await fixture.RunAsync(async () =>
        {
            var intent = (await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token))!;
            await fixture.Tasks.CommitAsync(intent.Recover(), intent.Revision.Value, fixture.Token);
        });
        fixture.Reopen();
        (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.Generation.Value.Should().Be(1);
        var stale = () => fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request, once.Id, once.Revision, fixture.Token));
        await stale.Should().ThrowAsync<InvalidDataException>();
        fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        await fixture.AdmitAsync(newSession: false);
        (await fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request, sessionGrant.Id, sessionGrant.Revision, fixture.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Consumed);
        (await fixture.RunAsync(() => fixture.Authorization.ConsumeAsync(fixture.Request, once.Id, once.Revision, fixture.Token)))
            .Outcome.Should().Be(HostInteractionOutcome.Denied);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Single(g => g.Id == once.Id).UseCount.Should().Be(0);
    }

    [WindowsFact]
    public async Task Root_intent_can_omit_invocation_but_supplied_wrong_invocation_never_admits_a_question()
    {
        using var fixture = new InteractionStorageFixture();
        var request = fixture.Request;
        await fixture.Tasks.InitializeAsync(fixture.Token);
        await fixture.Store.InitializeAsync(fixture.Token);
        await fixture.RunAsync(async () =>
        {
            var root = new HostRequest(request.RequestId, request.SessionId, request.TaskId, request.Origin);
            using var activity = Kora.Core.Diagnostics.HostActivity.BeginRoot(root,
                Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
            await fixture.Tasks.CommitAsync(new(root, new(1), HostTaskState.IntentRecorded), 0, fixture.Token);
        });
        await fixture.RunAsync(() => fixture.Store.CreateSessionAsync(request, fixture.Token));
        fixture.Proposal = new(request, new(Guid.NewGuid()), new(1), InteractionStorageFixture.Binding(),
            HostOperationEffect.BoundedRead, fixture.Time.Now.AddMinutes(5));
        await fixture.PublishAsync();
        (await fixture.PresentAsync()).Proposal!.Request.Should().Be(request);
        fixture.Request = InteractionStorageFixture.NewRequest();
        await fixture.AdmitAsync(newSession: true);
        var exact = fixture.Request;
        fixture.Request = new(exact.RequestId, exact.SessionId, exact.TaskId, exact.Origin, new(Guid.NewGuid()));
        var mismatched = () => fixture.RunAsync(() => fixture.Authorization.PresentAsync(fixture.Request, fixture.Token));
        await mismatched.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Out_of_snapshot_question_identity_collision_rolls_back_audit_and_preserves_the_other_owner()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var original = await fixture.PresentAsync();
        fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        await fixture.AdmitAsync(newSession: false);
        var count = fixture.Count("security_audit_events");
        var collision = original with
        {
            Key = new(fixture.Request, original.Key.QuestionId, new(1)),
            Proposal = fixture.Proposal,
        };
        var audit = new Kora.Core.Auditing.SecurityAuditEvent(Guid.NewGuid(),
            Kora.Core.Auditing.SecurityAuditCategory.SecurityApproval, "question.create",
            Kora.Core.Auditing.SecurityAuditOutcome.Succeeded, Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand,
            fixture.Request.TaskId.Value.ToString("D"));
        var attempt = () => fixture.RunAsync<HostInteractionDecision>(async () =>
        {
            using var policy = Kora.Core.Diagnostics.HostActivity.BeginAudit(fixture.Request, audit);
            return await fixture.Store.TransactAsync(fixture.Request, snapshot =>
                new(snapshot with { Questions = snapshot.Questions.Add(collision) },
                    new(HostInteractionOutcome.Presented, "question-presented", collision), audit), fixture.Token);
        });
        await attempt.Should().ThrowAsync<InvalidDataException>();
        fixture.Count("security_audit_events").Should().Be(count);
        (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single().Key.Should().Be(original.Key);
    }

    [Theory]
    [InlineData("DROP TABLE security_audit_events;")]
    [InlineData("CREATE TRIGGER deny_audit BEFORE INSERT ON security_audit_events BEGIN SELECT RAISE(ABORT,'denied'); END;")]
    [InlineData("DELETE FROM authority_head;")]
    [InlineData("UPDATE authority_head SET sequence=100;")]
    [InlineData("DELETE FROM security_audit_events WHERE sequence=1;")]
    [InlineData("UPDATE host_questions SET payload='{}';")]
    [InlineData("UPDATE host_questions SET payload=json_set(payload,'$.Options',NULL);")]
    [InlineData("UPDATE host_questions SET payload=json_set(payload,'$.Key',NULL);")]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.Audit',NULL) WHERE sequence=1;")]
    [InlineData("UPDATE security_audit_events SET envelope=json_set(envelope,'$.Changes',NULL) WHERE sequence=1;")]
    [InlineData("UPDATE host_questions SET revision=99;")]
    [InlineData("UPDATE host_questions SET payload=json_set(payload,'$.Text','tampered');")]
    [InlineData("PRAGMA user_version=99;")]
    public async Task Missing_schema_corrupt_payload_or_audit_failure_is_explicit_and_never_admits_partial_authority(string mutation)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await fixture.PresentAsync();
        fixture.Mutate(mutation);
        var before = File.ReadAllBytes(fixture.DatabasePath);
        var approve = () => fixture.RunAsync(() => fixture.Authorization.ApproveAsync(question.Key, new(["once"]),
            RequestOrigin.LocalUi, fixture.Token));
        await approve.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(fixture.DatabasePath).Should().Equal(before);
        fixture.Count("scoped_grants").Should().Be(0);
    }

    [Theory]
    [InlineData("database")]
    [InlineData("journal")]
    [InlineData("lease")]
    [InlineData("acl")]
    [InlineData("corrupt")]
    public async Task Private_owner_acl_corrupt_or_missing_managed_files_are_not_repaired_or_replaced(string failure)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var question = await fixture.PresentAsync();
        var target = fixture.DatabasePath;
        switch (failure)
        {
            case "database":
                File.Delete(target);
                break;
            case "journal":
                target += "-journal";
                File.Delete(target);
                break;
            case "lease":
                target = Path.Combine(Path.GetDirectoryName(target)!, "operation.lock");
                File.Delete(target);
                break;
            case "acl":
                var info = new FileInfo(target);
                var acl = info.GetAccessControl();
                acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                    FileSystemRights.Read, AccessControlType.Allow));
                info.SetAccessControl(acl);
                break;
            case "corrupt":
                File.WriteAllBytes(target, [1, 2, 3, 4]);
                break;
        }
        var exists = File.Exists(target);
        var before = exists ? File.ReadAllBytes(target) : [];
        var approve = () => fixture.RunAsync(() => fixture.Authorization.ApproveAsync(question.Key, new(["once"]),
            RequestOrigin.LocalUi, fixture.Token));
        if (string.Equals(failure, "acl", StringComparison.Ordinal))
        {
            await approve.Should().ThrowAsync<UnauthorizedAccessException>();
        }
        else
        {
            await approve.Should().ThrowAsync<InvalidDataException>();
        }
        File.Exists(target).Should().Be(exists);
        if (exists)
        {
            File.ReadAllBytes(target).Should().Equal(before);
        }
    }
}
