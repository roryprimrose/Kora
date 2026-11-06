using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Communication;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.Storage;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteSessionDispositionTests
{
    [Theory]
    [InlineData("terminal")]
    [InlineData("existing")]
    [InlineData("other-turn")]
    [InlineData("wrong-selection")]
    [InlineData("audit")]
    [InlineData("absent")]
    [InlineData("unmarked")]
    [InlineData("contradictory")]
    public async Task Stopped_disposition_accepts_only_inventoried_original_terminal_usage_and_preserves_failure_state(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var session = f.Request.SessionId;
        var request = WindowsSqliteConversationTests.Fresh(session);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var usage = new SessionModelUsage(provenance, "Selected model", null, TimeSpan.Zero, f.Time.Now);
        var target = await f.Store.ReadMetadataAsync(session, f.Token);
        Kora.Core.Storage.SessionDispositionPreview preview;
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RetainConversationMessageAsync(request, new(1), 0, "reviewed original", static () => true, f.Token);
            var dispatched = await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
            if (change is "existing" or "contradictory") { await f.Store.RecordModelUsageAsync(usage, f.Token); }
            var running = new ConversationCancellationTarget(Guid.NewGuid(), request, new(1),
                dispatched.Task.Revision, f.Time.Now, f.Time.Now.AddMinutes(1));
            preview = await f.Store.PreviewStopAndDeleteAsync(target, running, f.Token);
            preview.StoppedWork!.ModelUsage.Should().Be(change is "existing" or "contradictory" ? usage : null);
            preview.StoppedWork.PendingModelUsage.Should().Be(change is not ("existing" or "contradictory"));
            if (change is "unmarked")
            { preview = preview with { StoppedWork = preview.StoppedWork with { PendingModelUsage = false } }; }
            if (change is "contradictory")
            { preview = preview with { StoppedWork = preview.StoppedWork with { PendingModelUsage = true } }; }
            await f.Store.CompleteConversationExecutionAsync(request,
                new(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled, provenance), static () => true, f.Token);
            var observed = change switch
            {
                "other-turn" => usage with { Provenance = provenance with { Turn = new(Guid.NewGuid()) } },
                "wrong-selection" => usage with
                {
                    Provenance = provenance with
                    { Selection = new(ModelProviderIdentity.Ollama, new(Guid.NewGuid()), new(1)) }
                },
                _ => usage,
            };
            if (change is not "absent") { await f.Store.RecordModelUsageAsync(observed, f.Token); }
        }
        var control = WindowsSqliteConversationTests.Fresh(session);
        using var controlRoot = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(control, f.Token);
        var audits = f.Count("security_audit_events");
        if (change is "audit")
        {
            checkpoint.Audit = (_, _) => throw new IOException("required disposition audit failed");
        }
        var dispose = () => f.Store.DisposeSessionAsync(control, preview, static () => true, f.Token).AsTask();
        if (change is "terminal" or "existing")
        {
            var receipt = await dispose();
            receipt.Removed.Should().Be(preview);
            f.Count("session_model_usage").Should().Be(0);
            (await f.Store.ReadHistoryAsync(session, null, 50, f.Token)).Disposed.Should().BeTrue();
            using var lateRoot = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            await f.Store.Invoking(store => store.RecordModelUsageAsync(usage, f.Token).AsTask())
                .Should().ThrowAsync<InvalidDataException>();
        }
        else
        {
            if (change is "audit") { await dispose.Should().ThrowAsync<IOException>(); }
            else { await dispose.Should().ThrowAsync<InvalidOperationException>(); }
            f.Count("security_audit_events").Should().Be(audits);
            f.Count("session_model_usage").Should().Be(change is "absent" ? 0 : 1);
            (await f.Store.ReadMetadataAsync(session, f.Token)).Should().Be(target);
            (await f.Store.ReadConversationMessageAsync(session, request.RequestId, f.Token)).Body.Should().Be("reviewed original");
            (await f.Tasks.ReadTaskAsync(control.TaskId, f.Token))!.State.Should().Be(HostTaskState.IntentRecorded);
        }
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("audit")]
    [InlineData("digest")]
    [InlineData("payload")]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    public async Task Tampered_terminal_usage_cannot_mutate_disposition_or_create_success_audit(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var session = f.Request.SessionId;
        var request = WindowsSqliteConversationTests.Fresh(session);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var target = await f.Store.ReadMetadataAsync(session, f.Token);
        Kora.Core.Storage.SessionDispositionPreview preview;
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RetainConversationMessageAsync(request, new(1), 0, "retained original", static () => true, f.Token);
            var dispatched = await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
            preview = await f.Store.PreviewStopAndDeleteAsync(target, new(Guid.NewGuid(), request, new(1),
                dispatched.Task.Revision, f.Time.Now, f.Time.Now.AddMinutes(1)), f.Token);
            await f.Store.CompleteConversationExecutionAsync(request,
                new(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled, provenance), static () => true, f.Token);
            await f.Store.RecordModelUsageAsync(new(provenance, "Selected model", null, TimeSpan.Zero, f.Time.Now), f.Token);
        }
        var control = WindowsSqliteConversationTests.Fresh(session);
        using var controlRoot = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(control, f.Token);
        f.Mutate(fault switch
        {
            "generation" => "UPDATE session_model_usage SET generation=generation+1;",
            "audit" => "UPDATE session_model_usage SET audit_sequence=audit_sequence+1;",
            "digest" => "UPDATE session_model_usage SET digest=replace(digest,substr(digest,1,1),'z');",
            "payload" => "UPDATE session_model_usage SET payload=replace(payload,'Selected model','Tampered model');",
            "foreign" => "UPDATE session_model_usage SET session_id='00000000-0000-0000-0000-000000000001';",
            _ => """
                    INSERT INTO session_model_usage
                    SELECT '00000000-0000-0000-0000-000000000001',request_id,session_id,task_id,generation,audit_sequence,payload,digest
                    FROM session_model_usage;
                    """,
        });
        var audits = f.Count("security_audit_events");
        var messages = f.Count("conversation_messages");
        var usageRows = f.Count("session_model_usage");
        await f.Store.Invoking(store => store.DisposeSessionAsync(control, preview, static () => true, f.Token).AsTask())
            .Should().ThrowAsync<InvalidDataException>();
        f.Count("security_audit_events").Should().Be(audits);
        f.Count("conversation_messages").Should().Be(messages);
        f.Count("session_model_usage").Should().Be(usageRows);
        using var connection = f.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT generation FROM work_sessions WHERE session_id=$id;";
        command.Parameters.AddWithValue("$id", session.Value.ToString("D"));
        ((long)command.ExecuteScalar()!).Should().Be(target.Authority.Generation.Value);
        command.CommandText = "SELECT state FROM host_task_events WHERE task_id=$task ORDER BY revision DESC LIMIT 1;";
        command.Parameters.AddWithValue("$task", control.TaskId.Value.ToString("D"));
        ((long)command.ExecuteScalar()!).Should().Be((long)HostTaskState.IntentRecorded);
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("short")]
    [InlineData("hot")]
    [InlineData("copy")]
    public async Task Post_commit_cleanup_failure_never_returns_a_receipt_or_promises_rollback(string fault)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var preview = await fixture.Store.PreviewDispositionAsync(fixture.Request.SessionId, new(1), 0, fixture.Token);
        fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        await fixture.RunAsync(async () => await fixture.Tasks.CommitAsync(
            new(fixture.Request, new(1), HostTaskState.IntentRecorded), 0, fixture.Token));
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        var directory = new RestrictedStorageDirectory(fixture.Paths, includeKeys: false, partitionName: HostInteractionSchema.Partition);
        var copy = Path.Combine(directory.Artifacts, "post-commit-owned-copy");
        FileStream? pin = null;
        var injected = false;
        checkpoint.Commit = (connection, _) =>
        {
            connection.StateChange += (_, state) =>
            {
                if (state.CurrentState != System.Data.ConnectionState.Closed) { return; }
                injected = true;
                switch (fault)
                {
                    case "locked":
                        pin = new FileStream(fixture.DatabasePath + "-journal", FileMode.Open, FileAccess.Read, FileShare.Read);
                        break;
                    case "short": File.WriteAllBytes(fixture.DatabasePath + "-journal", [0]); break;
                    case "hot": File.WriteAllBytes(fixture.DatabasePath + "-journal", [1, 2, 3, 4, 5, 6, 7, 8]); break;
                    case "copy":
                        using (var artifact = directory.CreateNewFile(copy))
                        {
                            artifact.WriteByte(1);
                            artifact.Flush(flushToDisk: true);
                        }
                        break;
                }
            };
        };
        try
        {
            var removal = () => fixture.RunAsync(() => fixture.Store.DisposeSessionAsync(fixture.Request, preview, () => true, fixture.Token));
            await removal.Should().ThrowAsync<IOException>().WithMessage("*committed*unconfirmed*do not infer rollback*");
            injected.Should().BeTrue();
        }
        finally
        {
            pin?.Dispose();
            checkpoint.Commit = null;
            // Remove only the injected fixture fault, not the already committed disposition.
            if (File.Exists(copy)) { File.Delete(copy); }
            using var journal = new FileStream(fixture.DatabasePath + "-journal", FileMode.Open, FileAccess.Write, FileShare.None);
            journal.SetLength(0);
            journal.Flush(flushToDisk: true);
        }
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.Generation.Value.Should().Be(2);
        (await fixture.Tasks.ReadTaskAsync(fixture.Request.TaskId, fixture.Token))!.State.Should().Be(HostTaskState.Succeeded);
        var replay = () => fixture.RunAsync(() => fixture.Store.DisposeSessionAsync(fixture.Request, preview, () => true, fixture.Token));
        await replay.Should().ThrowAsync<InvalidDataException>().WithMessage("*nonterminal intent*");
    }

    [WindowsFact]
    public async Task Uninventoried_copy_holds_even_a_counter_empty_disposition_before_mutation()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        await service.RenameAsync(fixture.Request.SessionId, new(1), 0, new("Kept private name"), RequestOrigin.LocalUi, fixture.Token);
        var preview = await PreviewAsync(fixture, service, 1);
        var directory = new RestrictedStorageDirectory(fixture.Paths, includeKeys: false, partitionName: HostInteractionSchema.Partition);
        var copy = Path.Combine(directory.Artifacts, "uninventoried-disposition-copy");
        await StorageFilePublication.WriteStagingAsync(directory, copy, File.ReadAllBytes(fixture.DatabasePath),
            StoragePublicationKind.Artifact, null, fixture.Token);
        var removal = () => service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        await removal.Should().ThrowAsync<InvalidDataException>();
        File.Exists(copy).Should().BeTrue();
        (await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token)).Should().Be(preview.Session);
    }

    [WindowsFact]
    public async Task Clear_or_unavailable_automatic_state_with_uncertain_manual_evidence_cannot_admit_disposition()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        foreach (var automatic in new[] { CallState.Clear, CallState.Unavailable })
        {
            var policy = new CallPolicyObservation(1, automatic, false, new(true, false), ManualControlEvidenceUnavailable: true);
            var access = new CallAccess(policy);
            var service = new SessionWorkspaceService(fixture.Store, new(fixture.Tasks), access,
                NullLogger<SessionWorkspaceService>.Instance);
            var act = () => PreviewAsync(fixture, service);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unchanged private*");
            access.CanInspect.Should().BeTrue();
            access.CanControl.Should().BeFalse();
            (await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token)).Authority.Generation.Value.Should().Be(1);
        }
        fixture.Count("host_tasks").Should().Be(1);
    }

    [WindowsFact]
    public async Task Lost_or_rolled_back_tombstone_fails_recovery_and_cannot_recreate_a_disposed_identity()
    {
        foreach (var corrupt in new[] { "DELETE FROM work_sessions;", "UPDATE work_sessions SET generation=1,state=0,audit_sequence=1;" })
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
            var preview = await PreviewAsync(fixture, service);
            await service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
            fixture.Mutate(corrupt);
            fixture.Reopen();
            var reopen = () => fixture.Store.InitializeAsync(fixture.Token).AsTask();
            await reopen.Should().ThrowAsync<InvalidDataException>();
            var read = () => fixture.Store.ReadMetadataPageAsync(null, 25, fixture.Token).AsTask();
            await read.Should().ThrowAsync<InvalidDataException>();
            var late = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
            using var host = HostActivity.BeginRoot(late, HostActivityLayer.Application, HostOperation.Request);
            var recreate = () => fixture.Tasks.CommitAsync(new(late, new(1), HostTaskState.IntentRecorded), 0, fixture.Token).AsTask();
            await recreate.Should().ThrowAsync<InvalidDataException>();
        }
    }

    [WindowsFact]
    public async Task Cancelled_admitted_wait_is_removed_without_dangling_question_binding_and_late_answer_stays_denied()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        fixture.Proposal = null!;
        fixture.ObservationRevision = (await fixture.RunAsync(() => fixture.Store.PublishTrustedSnapshotAsync(
            fixture.Request, fixture.Policy, null, fixture.ObservationRevision, fixture.Token))).Value;
        var question = (await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
            LocalVersionWait.CreateSpec(), fixture.Time.Now.AddMinutes(5), fixture.Token))).Question!;
        await fixture.RunAsync(async () => await fixture.Store.AdmitVersionWaitAsync(question.Key, fixture.Token));
        var observed = (await fixture.Store.ReadTaskAsync(fixture.Request.SessionId, fixture.Request.TaskId, fixture.Token))!;
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        await service.CancelTaskAsync(new(fixture.Request.SessionId, fixture.Request.TaskId, observed.Task.Revision,
            observed.Generation, question.Key.QuestionId, question.Key.Revision), RequestOrigin.LocalUi, () => true, fixture.Token);
        var preview = await PreviewAsync(fixture, service);
        preview.Waits.Should().Be(1);
        await service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        fixture.Count("host_task_waits").Should().Be(0);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var answer = () => fixture.RunAsync(() => fixture.Questions.SubmitAsync(question.Key, new(["show"]),
            RequestOrigin.LocalUi, fixture.Token));
        await answer.Should().ThrowAsync<InvalidDataException>();
        (await new HostTaskCoordinator(fixture.Tasks).RecoverAsync(10, fixture.Token)).Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Native_two_step_disposition_removes_live_content_preserves_unrelated_and_perpetual_and_reopens_without_replay()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await fixture.GrantAsync("session");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        await fixture.AdmitAsync(newSession: false);
        var perpetual = await fixture.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var target = fixture.Request.SessionId;
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
        await service.RenameAsync(target, new(1), 0, new("Intentional content"), RequestOrigin.LocalUi, fixture.Token);
        fixture.Request = InteractionStorageFixture.NewRequest();
        await fixture.AdmitAsync(newSession: true);
        var unrelated = await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token);
        var unrelatedTasks = await fixture.Store.ReadTaskPageAsync(fixture.Request.SessionId, null, 25, fixture.Token);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        sink.Initialize();
        var evidence = new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), access, fixture.Time,
            NullLogger<DurableEvidenceQuery>.Instance);
        var state = new SessionsViewModel(service, evidence, access, NullLogger<SessionsViewModel>.Instance);
        await state.RefreshAsync();
        await state.SelectAsync(state.Sessions.Single(row => row.Authority.SessionId == target));
        state.CanConfirmDisposition.Should().BeFalse();
        await state.PreviewDispositionAsync();
        state.Detail.Should().Contain(target.Value.ToString("D")).And.Contain("Intentional", because: "the preview scope includes intentional live content")
            .And.Contain("not forensic deletion");
        state.CanConfirmDisposition.Should().BeTrue();
        var auditCount = fixture.Count("security_audit_events");
        await state.ConfirmDispositionAsync();
        state.Status.Should().StartWith("Committed logical disposition");
        state.CanConfirmDisposition.Should().BeFalse();
        fixture.Count("security_audit_events").Should().Be(auditCount + 1);
        fixture.Count("session_metadata").Should().Be(0);
        (await fixture.Store.ReadQuestionsAsync(target, fixture.Token)).Should().BeEmpty();
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle().Which.Should().Be(perpetual);
        (await fixture.Store.ReadMetadataAsync(unrelated.Authority.SessionId, fixture.Token)).Should().Be(unrelated);
        (await fixture.Store.ReadTaskPageAsync(unrelated.Authority.SessionId, null, 25, fixture.Token)).Should().BeEquivalentTo(unrelatedTasks);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await fixture.Store.ReadMetadataPageAsync(null, 25, fixture.Token)).Records.Should().ContainSingle().Which.Should().Be(unrelated);
        (await fixture.Store.ReadSessionAsync(target, fixture.Token))!.Generation.Value.Should().Be(2);
        var read = () => fixture.Store.ReadMetadataAsync(target, fixture.Token).AsTask();
        await read.Should().ThrowAsync<InvalidOperationException>();
        var recovery = await new HostTaskCoordinator(fixture.Tasks).RecoverAsync(10, fixture.Token);
        recovery.Should().ContainSingle().Which.Request.SessionId.Should().Be(unrelated.Authority.SessionId);
        using var raw = fixture.OpenRaw();
        using var command = raw.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events ORDER BY sequence DESC LIMIT 1;";
        var audit = (string)command.ExecuteScalar()!;
        audit.Should().Contain("session.disposition").And.Contain("TraceId").And.Contain("SpanId")
            .And.NotContain("Intentional content");
        state.Close();
    }

    [WindowsFact]
    public async Task Exact_preview_denies_unknown_stale_generation_and_stale_metadata_without_new_intent()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var count = fixture.Count("host_tasks");
        var unknown = () => fixture.Store.PreviewDispositionAsync(new(Guid.NewGuid()), new(1), 0, fixture.Token).AsTask();
        await unknown.Should().ThrowAsync<InvalidDataException>();
        var staleGeneration = () => fixture.Store.PreviewDispositionAsync(fixture.Request.SessionId, new(2), 0, fixture.Token).AsTask();
        await staleGeneration.Should().ThrowAsync<InvalidOperationException>();
        var staleMetadata = () => fixture.Store.PreviewDispositionAsync(fixture.Request.SessionId, new(1), 1, fixture.Token).AsTask();
        await staleMetadata.Should().ThrowAsync<InvalidOperationException>();
        fixture.Count("host_tasks").Should().Be(count);
    }

    [WindowsFact]
    public async Task Nonterminal_unknown_and_unresolved_question_work_cannot_be_disposed_even_by_low_level_store()
    {
        foreach (var state in new[] { HostTaskState.IntentRecorded, HostTaskState.DispatchRecorded, HostTaskState.Unknown, HostTaskState.Succeeded })
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            if (state == HostTaskState.Succeeded)
            {
                await fixture.RunAsync(() => fixture.Questions.CreateAsync(fixture.Request,
                    new("Unresolved", QuestionKind.Text, [], maximumTextLength: 16), fixture.Time.Now.AddMinutes(5), fixture.Token));
            }
            if (state != HostTaskState.IntentRecorded) { await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, state); }
            var act = () => fixture.Store.PreviewDispositionAsync(fixture.Request.SessionId, new(1), 0, fixture.Token).AsTask();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*blocked*");
            (await fixture.Store.ReadSessionAsync(fixture.Request.SessionId, fixture.Token))!.Generation.Value.Should().Be(1);
        }
    }

    [WindowsFact]
    public async Task Completed_new_work_and_rename_invalidate_preview_even_when_idle_again()
    {
        foreach (var rename in new[] { false, true })
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
            var preview = await PreviewAsync(fixture, service);
            if (rename)
            {
                await service.RenameAsync(fixture.Request.SessionId, new(1), 0, new("Changed"), RequestOrigin.LocalUi, fixture.Token);
            }
            else
            {
                fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
                await fixture.AdmitAsync(newSession: false);
                await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            }
            var act = () => service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
            await act.Should().ThrowAsync<InvalidOperationException>();
            (await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token)).Authority.Generation.Value.Should().Be(1);
        }
    }

    [WindowsFact]
    public async Task Audit_failure_cancellation_and_gate_race_roll_back_all_live_rows_and_tombstone_then_recovery_is_truthful()
    {
        foreach (var mode in new[] { "audit", "cancel", "gate" })
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            await fixture.GrantAsync("session");
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            var checkpoint = new InteractionTransactionCheckpoint();
            fixture.Reopen(checkpoint);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
            var preview = await PreviewAsync(fixture, service);
            var auditCount = fixture.Count("security_audit_events");
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
            if (string.Equals(mode, "audit", StringComparison.Ordinal))
            {
                checkpoint.Audit = (_, _) => throw new IOException("required audit unavailable");
            }
            else
            {
                checkpoint.Commit = (_, _) =>
                {
                    if (string.Equals(mode, "cancel", StringComparison.Ordinal)) { cancellation.Cancel(); }
                    else { access.ControlRevision++; }
                };
            }
            var act = () => service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, cancellation.Token);
            await act.Should().ThrowAsync<Exception>();
            checkpoint.Audit = null;
            checkpoint.Commit = null;
            fixture.Reopen();
            await fixture.Store.InitializeAsync(fixture.Token);
            (await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token)).Authority.Generation.Value.Should().Be(1);
            (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Should().ContainSingle();
            (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle();
            fixture.Count("security_audit_events").Should().Be(auditCount);
            var recovery = await new HostTaskCoordinator(fixture.Tasks).RecoverAsync(10, fixture.Token);
            recovery.Should().OnlyContain(task => task.State == HostTaskState.Interrupted);
            var replay = () => service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
            await replay.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [WindowsFact]
    public async Task Corrupt_or_missing_authority_never_becomes_an_empty_disposable_session()
    {
        foreach (var mode in new[] { "missing", "audit", "metadata", "task" })
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
            await service.RenameAsync(fixture.Request.SessionId, new(1), 0, new("Kept"), RequestOrigin.LocalUi, fixture.Token);
            var preview = await PreviewAsync(fixture, service, 1);
            switch (mode)
            {
                case "missing": File.Delete(fixture.DatabasePath); break;
                case "audit": fixture.Mutate("UPDATE authority_head SET sequence=0;"); break;
                case "metadata": fixture.Mutate("DELETE FROM session_metadata;"); break;
                case "task": fixture.Mutate("DELETE FROM host_task_events;"); break;
            }
            var act = () => service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
            await act.Should().ThrowAsync<InvalidDataException>();
            if (string.Equals(mode, "missing", StringComparison.Ordinal)) { File.Exists(fixture.DatabasePath).Should().BeFalse(); }
        }
    }

    [WindowsFact]
    public async Task Disposition_owns_shared_lease_and_late_task_append_loses_after_commit()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var checkpoint = new InteractionTransactionCheckpoint();
        fixture.Reopen(checkpoint);
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        var preview = await PreviewAsync(fixture, service);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        checkpoint.Commit = (_, _) => { entered.Set(); release.Wait(fixture.Token); };
        var disposition = service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        await Task.Run(() => entered.Wait(fixture.Token), fixture.Token);
        var late = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
        using var host = HostActivity.BeginRoot(late, HostActivityLayer.Application, HostOperation.Request);
        var append = fixture.Tasks.CommitAsync(new(late, new(1), HostTaskState.IntentRecorded), 0, fixture.Token).AsTask();
        append.IsCompleted.Should().BeFalse();
        release.Set();
        (await disposition).Generation.Value.Should().Be(2);
        var act = () => append;
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*disposed*");
        checkpoint.Commit = null;
        var create = () => fixture.Store.CreateSessionAsync(late, fixture.Token).AsTask();
        await create.Should().ThrowAsync<Exception>();
        var snapshot = () => fixture.Store.PublishTrustedSnapshotAsync(late, fixture.Policy, null, 0, fixture.Token).AsTask();
        await snapshot.Should().ThrowAsync<Exception>();
        var interaction = () => fixture.Store.TransactAsync(late, _ => throw new InvalidOperationException("callback must not run"), fixture.Token).AsTask();
        await interaction.Should().ThrowAsync<Exception>();
        (await fixture.Tasks.ReadIncompleteAsync(10, fixture.Token)).Should().BeEmpty();
    }

    private static async Task<SessionDispositionPreview> PreviewAsync(InteractionStorageFixture fixture,
        SessionWorkspaceService service, long metadataRevision = 0)
    {
        using var viewer = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Presentation);
        return await service.PreviewDispositionAsync(fixture.Request.SessionId, new(1), metadataRevision, fixture.Token);
    }

    private sealed class CallAccess(CallPolicyObservation policy) : ISessionWorkspaceAccess
    {
        public bool CanInspect => true;
        public bool CanControl => !policy.IsProtected;
        public long ControlRevision => policy.Revision;
    }
}