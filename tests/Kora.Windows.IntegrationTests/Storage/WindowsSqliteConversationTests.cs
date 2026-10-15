using System.Text;

using AwesomeAssertions;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Fact]
    public async Task ActualPrivateStoreRetainsExactOriginalBodyTaskAndRequestedTerminalAuditAcrossRestartWithoutReplay()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        const string body = " private conversation marker e\u0301🙂\r\n  exact ending  ";
        var request = Fresh(f.Request.SessionId);
        ConversationSubmissionReceipt retained;
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            retained = await f.Store.RetainConversationMessageAsync(request, new(1), 0, body, static () => true, f.Token);
            retained.Task.State.Should().Be(HostTaskState.IntentRecorded);
            retained.Message.Body.Should().Be(body);
            var terminal = await f.Store.CompleteConversationSubmissionAsync(request,
                ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel, static () => true, f.Token);
            terminal.Task.State.Should().Be(HostTaskState.Unavailable);
            terminal.Message.Body.Should().Be(body);
            terminal.Message.Sequence.Should().Be(1);
        }
        var audits = AuditText(f);
        audits.Should().Contain("conversation.submit").And.Contain("conversation.terminal").And.Contain("\"Outcome\":0")
            .And.Contain("unregisteredmodel").And.NotContain("private conversation marker");
        var before = f.Count("host_tasks");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var page = await f.Store.ReadConversationPageAsync(request.SessionId, null, 50, f.Token);
        page.Messages.Single().Body.Should().Be(body);
        page.Messages.Single().Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        f.Count("host_tasks").Should().Be(before);
        var work = await f.Store.ReadWorkAsync(request.SessionId, 0, new(), f.Token);
        System.Text.Json.JsonSerializer.Serialize(work).Should().NotContain("private conversation marker");
        (await f.Store.ReadMetadataAsync(request.SessionId, f.Token)).Authority.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SavedDraftExplicitRevisionClearReloadAndStorageReadbackRemainIndependentOfMessagesAndActivity()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var session = f.Request.SessionId;
        var activity = await f.Store.ReadRetentionAsync(session, f.Token);
        var initial = await f.Store.ReadConversationDraftAsync(session, f.Token);
        initial.Draft.Body.Should().BeNull();
        var saved = await Save(f, initial, " saved-only marker\r\n🙂 ");
        saved.Draft.Revision.Should().Be(1);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var reopened = await f.Store.ReadConversationDraftAsync(session, f.Token);
        reopened.Should().Be(saved);
        f.Count("conversation_messages").Should().Be(0);
        var stale = () => Save(f, initial, "must not overwrite");
        await stale.Should().ThrowAsync<InvalidOperationException>();
        var cleared = await Save(f, reopened, null);
        cleared.Draft.Revision.Should().Be(2);
        cleared.Draft.Body.Should().BeNull();
        var replacement = await Save(f, cleared, "new draft");
        replacement.Draft.Revision.Should().Be(3);
        (await f.Store.ReadRetentionAsync(session, f.Token)).LastMeaningfulActivity.Should().Be(activity.LastMeaningfulActivity);
        Encoding.UTF8.GetString(File.ReadAllBytes(f.DatabasePath)).Should().NotContain("saved-only marker");
        new FileInfo(f.DatabasePath + "-journal").Length.Should().Be(0);
        AuditText(f).Should().NotContain("saved-only marker");
    }

    [Fact]
    public async Task InterruptedOriginalSubmissionIsPreservedAndRecoversWithoutDispatchOrDuplicateAppend()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var request = Fresh(f.Request.SessionId);
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            var original = await f.Store.RetainConversationMessageAsync(request, new(1), 0, "interrupt marker", static () => true, f.Token);
            var duplicate = await f.Store.RetainConversationMessageAsync(request, new(1), 0, "interrupt marker", static () => true, f.Token);
            duplicate.Should().Be(original);
            var conflict = () => f.Store.RetainConversationMessageAsync(request, new(1), 0, "different", static () => true, f.Token).AsTask();
            await conflict.Should().ThrowAsync<InvalidOperationException>();
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var recovered = await new Kora.Application.Hosting.HostTaskCoordinator(f.Tasks).RecoverAsync(100, f.Token);
        recovered.Single().State.Should().Be(HostTaskState.Interrupted);
        var page = await f.Store.ReadConversationPageAsync(request.SessionId, null, 25, f.Token);
        page.Messages.Single().Body.Should().Be("interrupt marker");
        page.Messages.Single().Reason.Should().Be(ModelTurnReason.HostInterrupted);
        f.Count("conversation_messages").Should().Be(1);
    }

    [Fact]
    public async Task PageBudgetUsesCompleteSerializedBytesAndOriginalSequenceCeilingWithoutTruncatingAnyBody()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var session = f.Request.SessionId;
        var body = new string('\u0800', 4096);
        for (var index = 0; index < 4; index++) { await Submit(f, session, body); }
        var page = await f.Store.ReadConversationPageAsync(session, null, 50, f.Token);
        page.Messages.Should().HaveCount(2);
        ConversationPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(65536);
        page.Messages.Should().OnlyContain(message => message.Body == body);
        await Submit(f, session, "later");
        var next = await f.Store.ReadConversationPageAsync(session, page.Next, 50, f.Token);
        next.Ceiling.Should().Be(4);
        next.Messages.Select(message => message.Sequence).Should().Equal(3, 4);
        next.Next.Should().BeNull();
        var wrong = page.Next! with { SessionId = new(Guid.NewGuid()) };
        await f.Store.Invoking(store => store.ReadConversationPageAsync(session, wrong, 25, f.Token).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ChangedTerminalStatusWithinTheOriginalCeilingInvalidatesHeadRevisionRatherThanMixingSnapshots()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var session = f.Request.SessionId;
        await Submit(f, session, "first");
        await Submit(f, session, "second");
        var pending = Fresh(session);
        using var root = HostActivity.BeginRoot(pending, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RetainConversationMessageAsync(pending, new(1), 0, "pending original", static () => true, f.Token);
        var page = await f.Store.ReadConversationPageAsync(session, null, 1, f.Token);
        await f.Store.CompleteConversationSubmissionAsync(pending, ModelTurnOutcome.Unavailable,
            ModelTurnReason.UnregisteredModel, static () => true, f.Token);
        await f.Store.Invoking(store => store.ReadConversationPageAsync(session, page.Next, 1, f.Token).AsTask())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*terminal status changed*");
    }

    [Theory]
    [InlineData("draft-missing")]
    [InlineData("draft-unknown")]
    [InlineData("draft-body")]
    [InlineData("message-missing")]
    [InlineData("message-body")]
    [InlineData("message-sequence")]
    public async Task MissingAuditedOrCorruptConversationNeverBecomesEmptySuccess(string corruption)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var session = f.Request.SessionId;
        await Save(f, await f.Store.ReadConversationDraftAsync(session, f.Token), "stored draft");
        await Submit(f, session, "original message");
        f.Mutate(corruption switch
        {
            "draft-missing" => "DELETE FROM conversation_drafts;",
            "draft-unknown" => "UPDATE conversation_drafts SET payload='{}';",
            "draft-body" => "UPDATE conversation_drafts SET payload=json_set(payload,'$.Body','substituted');",
            "message-missing" => "DELETE FROM conversation_messages;",
            "message-body" => "UPDATE conversation_messages SET payload=json_set(payload,'$.Body','substituted');",
            _ => "UPDATE conversation_messages SET sequence=2;",
        });
        if (corruption.StartsWith("draft-", StringComparison.Ordinal))
        {
            await f.Store.Invoking(store => store.ReadConversationDraftAsync(session, f.Token).AsTask())
                .Should().ThrowAsync<InvalidDataException>();
        }
        else
        {
            await f.Store.Invoking(store => store.ReadConversationPageAsync(session, null, 25, f.Token).AsTask())
                .Should().ThrowAsync<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalPhysicalCommitAdmissionOrCancellationFailurePreservesDraftAndPreventsMessageAppend(bool cancel)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var session = f.Request.SessionId;
        await Save(f, await f.Store.ReadConversationDraftAsync(session, f.Token), "held draft");
        using var cancellation = new CancellationTokenSource();
        var admitted = true;
        f.Reopen(new Checkpoint(() => { if (cancel) { cancellation.Cancel(); } else { admitted = false; } }));
        var request = Fresh(session);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var submit = () => f.Store.RetainConversationMessageAsync(request, new(1), 0, "held draft", () => admitted, cancellation.Token).AsTask();
        await submit.Should().ThrowAsync<Exception>();
        f.Count("conversation_messages").Should().Be(0);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadConversationDraftAsync(session, f.Token)).Draft.Body.Should().Be("held draft");
        (await f.Tasks.ReadTaskAsync(request.TaskId, f.Token)).Should().BeNull("intent and retained body roll back atomically before COMMIT");
    }

    [Fact]
    public async Task ExactDispositionInventoriesConversationCellsAndCommittedJournalWithoutAffectingAnotherSession()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var session = f.Request.SessionId;
        await Save(f, await f.Store.ReadConversationDraftAsync(session, f.Token), "erase draft marker");
        await Submit(f, session, "erase message marker");
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await FinishInitial(f);
        var other = f.Request.SessionId;
        await Submit(f, other, "keep marker");
        var preview = await f.Store.PreviewDispositionAsync(session, new(1), 0, f.Token);
        preview.ConversationMessages.Should().Be(1);
        preview.SavedConversationDrafts.Should().Be(1);
        var control = Fresh(session);
        using (var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(control, f.Token);
            await f.Store.DisposeSessionAsync(control, preview, static () => true, f.Token);
        }
        Encoding.UTF8.GetString(File.ReadAllBytes(f.DatabasePath)).Should().NotContain("erase draft marker").And.NotContain("erase message marker");
        new FileInfo(f.DatabasePath + "-journal").Length.Should().Be(0);
        (await f.Store.ReadConversationPageAsync(other, null, 25, f.Token)).Messages.Single().Body.Should().Be("keep marker");
        await f.Store.Invoking(store => store.ReadConversationPageAsync(session, null, 25, f.Token).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
    }

    [Fact]
    public async Task LegacySchemaTenMigrationPreservesEveryOldSourceByteAndAuditAndMissingLegacyDraftMeansNone()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var audit = AuditText(f);
        var history = await f.Store.ReadHistoryAsync(f.Request.SessionId, null, 50, f.Token);
        f.Mutate("DROP TABLE conversation_drafts; DROP TABLE conversation_messages; PRAGMA user_version=10;");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        AuditText(f).Should().Be(audit);
        (await f.Store.ReadHistoryAsync(f.Request.SessionId, null, 50, f.Token)).Records.Should().Equal(history.Records);
        (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Should().Be(new SavedConversationDraft(f.Request.SessionId, 0, null));
    }

    [Fact]
    public async Task DoneReadsKeepOriginalDataWithoutActivityRenewalOrResumeAndSavedDraftRequiresNewCurrentGenerationGesture()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var session = f.Request.SessionId;
        await Submit(f, session, "retained original");
        var originalDraft = await Save(f, await f.Store.ReadConversationDraftAsync(session, f.Token), "saved data only");
        var control = Fresh(session);
        using (var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RecordControlIntentAsync(control, f.Token);
            await f.Store.ChangeIdleLifecycleAsync(control, new(1), false, static () => true, f.Token);
            var intent = (await f.Tasks.ReadTaskAsync(control.TaskId, f.Token))!;
            await f.Tasks.CommitAsync(intent.Next(HostTaskState.Succeeded), intent.Revision.Value, f.Token);
        }
        var activity = await f.Store.ReadRetentionAsync(session, f.Token);
        f.Time.Now = f.Time.Now.AddHours(1);
        var page = await f.Store.ReadConversationPageAsync(session, null, 25, f.Token);
        page.Session.IsActive.Should().BeFalse();
        page.Messages.Single().Body.Should().Be("retained original");
        (await f.Store.ReadConversationDraftAsync(session, f.Token)).Draft.Body.Should().Be("saved data only");
        (await f.Store.ReadRetentionAsync(session, f.Token)).LastMeaningfulActivity.Should().Be(activity.LastMeaningfulActivity);
        await this.Invoking(_ => Save(f, originalDraft, "must not overwrite")).Should().ThrowAsync<InvalidOperationException>();
        var denied = Fresh(session);
        using (var root = HostActivity.BeginRoot(denied, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.Invoking(store => store.RetainConversationMessageAsync(denied, new(2), 0, "no implicit resume",
                static () => true, f.Token).AsTask()).Should().ThrowAsync<InvalidOperationException>();
        }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        f.Count("conversation_messages").Should().Be(1);
        (await f.Store.ReadConversationDraftAsync(session, f.Token)).Session.IsActive.Should().BeFalse();
    }

    internal static HostRequest Fresh(HostId<SessionIdentity> session) =>
        new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi);

    internal static async Task FinishInitial(InteractionStorageFixture f)
    {
        var task = (await f.Tasks.ReadTaskAsync(f.Request.TaskId, f.Token))!;
        await f.RunAsync(() => f.Tasks.CommitAsync(task.Next(HostTaskState.Succeeded), task.Revision.Value, f.Token).AsTask());
    }

    internal static async Task<ConversationDraftSnapshot> Save(InteractionStorageFixture f, ConversationDraftSnapshot expected, string? body)
    {
        var request = Fresh(expected.Session.SessionId);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(request, f.Token);
        try { return await f.Store.CommitConversationDraftAsync(request, expected, body, static () => true, f.Token); }
        catch
        {
            var task = (await f.Tasks.ReadTaskAsync(request.TaskId, f.Token))!;
            if (!task.IsTerminal) { await f.Tasks.CommitAsync(task.Next(HostTaskState.Denied), task.Revision.Value, f.Token); }
            throw;
        }
    }

    internal static async Task<ConversationSubmissionReceipt> Submit(InteractionStorageFixture f, HostId<SessionIdentity> session, string body)
    {
        var request = Fresh(session);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RetainConversationMessageAsync(request, new(1), 0, body, static () => true, f.Token);
        return await f.Store.CompleteConversationSubmissionAsync(request,
            ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel, static () => true, f.Token);
    }

    internal static string AuditText(InteractionStorageFixture f)
    {
        using var connection = f.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events ORDER BY sequence;";
        using var rows = command.ExecuteReader();
        var text = new StringBuilder();
        while (rows.Read()) { text.AppendLine(rows.GetString(0)); }
        return text.ToString();
    }

    private sealed class Checkpoint(Action action) : IHostInteractionTransactionCheckpoint
    {
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) => action();
    }
}
