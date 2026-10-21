using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task NativeNewRequestCommitsIndependentGenericTargetBeforeExactFrozenBodyAndTruthfulProductionOutcome()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            await composed.Model.RefreshAsync();
            await composed.Model.SelectAsync(composed.Model.Sessions.Single());
            composed.Model.ComposerBody = "dirty named buffer never transferred";
            var previous = f.Request.SessionId;
            using var composer = composed.NewComposer();
            var model = new NewRequestViewModel(composer);
            var window = new NewRequestWindow(model, () => Task.CompletedTask, _ => { });
            window.Show();
            try
            {
                const string body = "  unrelated original e\u0301🙂\r\nsame bytes  ";
                window.FindControl<TextBox>("NewRequestBody")!.Text = body;
                composer.Body.Should().Be(body);
                window.FindControl<Button>("SubmitNewRequest")!.IsEnabled.Should().BeFalse();
                window.FindControl<Button>("CreateTarget")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                var target = composer.Target!;
                target.Authority.SessionId.Should().NotBe(previous);
                model.TargetLabel.Should().Contain(target.Authority.SessionId.Value.ToString("D"))
                    .And.Contain("New conversation").And.Contain("generation 1").And.Contain("metadata revision 1");
                f.Count("conversation_messages").Should().Be(0);
                (await f.Store.ReadConversationDraftAsync(target.Authority.SessionId, f.Token)).Draft.Body.Should().BeNull();
                (await f.Store.ReadConversationDraftAsync(previous, f.Token)).Draft.Body.Should().BeNull();
                window.FindControl<Button>("ReviewNewRequest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.ReviewedBody.Should().Be(body);
                window.FindControl<Button>("SubmitNewRequest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                var message = (await f.Store.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages.Single();
                message.Body.Should().Be(body);
                message.Request.Should().Be(composer.Submission);
                message.Outcome.Should().Be(Kora.Core.Dependencies.ModelTurnOutcome.Unavailable);
                composer.Status.Should().Contain("HOST Unavailable").And.Contain("UnregisteredModel");
                composer.Body.Should().BeEmpty();
                composed.Model.ComposerBody.Should().Be("dirty named buffer never transferred");
                composed.Model.SelectedSessionRecord!.Authority.SessionId.Should().Be(previous);
                f.Count("host_questions").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
                var before = await f.Store.ReadRetentionAsync(target.Authority.SessionId, f.Token);
                var audits = f.Count("security_audit_events");
                window.FindControl<Button>("InspectNewRequest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await window.Operation;
                composer.Status.Should().Contain("task Unavailable");
                (await f.Store.ReadRetentionAsync(target.Authority.SessionId, f.Token)).Should().Be(before);
                f.Count("security_audit_events").Should().Be(audits);
                using var connection = f.OpenRaw();
                using var read = connection.CreateCommand();
                read.CommandText = "SELECT group_concat(envelope) FROM security_audit_events;";
                ((string)read.ExecuteScalar()!).Should().Contain("session.create.named")
                    .And.Contain("conversation.submit").And.Contain("conversation.terminal").And.NotContain(body);
            }
            finally { window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshFallbackNeverReusesSelectedActiveOrDoneSession(bool done)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        if (done) { await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token); }
        await composed.Model.RefreshAsync();
        await composed.Model.SelectAsync(composed.Model.Sessions.Single());
        using var first = composed.NewComposer();
        using var second = composed.NewComposer();
        first.Body = "first unrelated";
        second.Body = "second unrelated";
        await first.Create();
        await second.Create();
        first.Target!.Authority.SessionId.Should().NotBe(f.Request.SessionId).And.NotBe(second.Target!.Authority.SessionId);
        first.Target.Metadata!.Name.Value.Should().Be("New conversation");
        second.Target.Metadata!.Name.Should().Be(first.Target.Metadata.Name);
        first.Review();
        await first.Submit();
        second.Review();
        await second.Submit();
        (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
        (await f.Store.ReadConversationPageAsync(first.Target.Authority.SessionId, null, 25, f.Token)).Messages.Single().Body.Should().Be("first unrelated");
        (await f.Store.ReadConversationPageAsync(second.Target.Authority.SessionId, null, 25, f.Token)).Messages.Single().Body.Should().Be("second unrelated");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirtyNewRequestCloseRequiresExplicitDiscardOrCancelAndLeavesDisclosedEmptySession(bool discard)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composed = new Composition(f);
            using var composer = composed.NewComposer();
            var window = new NewRequestWindow(new(composer), () => Task.CompletedTask, _ => { });
            window.Show();
            composer.Body = "memory only";
            await composer.Create();
            var id = composer.Target!.Authority.SessionId;
            composer.Review();
            var decisions = 0;
            window.DecideDraft = () => { decisions++; return Task.FromResult(discard ? ComposerDraftDecision.Discard : ComposerDraftDecision.Cancel); };
            window.Close();
            decisions.Should().Be(1);
            window.IsVisible.Should().Be(!discard);
            composer.CanSubmit.Should().BeFalse();
            composer.Body.Should().Be(discard ? string.Empty : "memory only");
            (await f.Store.ReadMetadataAsync(id, f.Token)).Authority.IsActive.Should().BeTrue();
            f.Count("conversation_messages").Should().Be(0);
            f.Count("conversation_drafts").Should().Be(0);
            window.ClosePrivate();
            composer.Body.Should().BeEmpty();
            await composer.Create();
            await composer.Submit();
            f.Count("conversation_messages").Should().Be(0);
        });
    }

    [Theory]
    [InlineData("create", "edit")]
    [InlineData("create", "cancel")]
    [InlineData("create", "revision")]
    [InlineData("create", "control")]
    [InlineData("create", "private")]
    [InlineData("create", "close")]
    [InlineData("submit", "edit")]
    [InlineData("submit", "cancel")]
    [InlineData("submit", "revision")]
    [InlineData("submit", "control")]
    [InlineData("submit", "private")]
    [InlineData("submit", "close")]
    public async Task NewRequestOriginalAdmissionIsFencedAtActualPrivateSqliteCommitAndLateCallbacksCannotRetry(string operation, string revoke)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var checkpoint = new NewRequestCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        await using var composed = new Composition(f);
        using var composer = composed.NewComposer();
        composer.Body = "original exact";
        if (operation is "submit") { await composer.Create(); composer.Review(); }
        checkpoint.Action = operation is "create" ? "session.create.named" : "conversation.submit";
        checkpoint.Commit = () =>
        {
            if (revoke is "edit") { composer.Body = "changed at commit"; }
            if (revoke is "cancel") { composer.Cancel(); }
            if (revoke is "revision") { composed.Access.ControlRevision++; }
            if (revoke is "control") { composed.Access.CanControl = false; }
            if (revoke is "private") { composed.Access.CanInspect = false; composed.Access.CanControl = false; }
            if (revoke is "close") { composer.Dispose(); }
        };
        var sessions = f.Count("work_sessions");
        if (operation is "create") { await composer.Create(); }
        else { await composer.Submit(); }
        checkpoint.Calls.Should().Be(1);
        f.Count("work_sessions").Should().Be(sessions);
        f.Count("conversation_messages").Should().Be(0);
        f.Count("conversation_drafts").Should().Be(0);
        composer.CanSubmit.Should().BeFalse();
        if (revoke is "private" or "close") { composer.Body.Should().BeEmpty(); }
        else { composer.Body.Should().NotBeEmpty(); }
        composed.Access.CanInspect = true;
        composed.Access.CanControl = true;
        checkpoint.Commit = null;
        await composer.Create();
        composer.Review();
        await composer.Submit();
        f.Count("work_sessions").Should().Be(sessions);
        f.Count("conversation_messages").Should().Be(0);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("submit")]
    [InlineData("terminal")]
    [InlineData("credential-audit")]
    public async Task RequiredAuditFailurePreservesExactDraftAndKnownOrUnconfirmedRecoveryWithoutRetry(string operation)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var checkpoint = new NewRequestCheckpoint();
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        await using var composed = new Composition(f);
        using var composer = composed.NewComposer();
        composer.Body = operation is "credential-audit" ? "api_key=synthetic-only-value" : "private recovery draft";
        if (operation is not "create") { await composer.Create(); composer.Review(); }
        checkpoint.Action = operation switch
        {
            "create" => "session.create.named",
            "submit" => "conversation.submit",
            "terminal" => "conversation.terminal",
            _ => "conversation.credential-rejected",
        };
        checkpoint.Commit = () => throw new IOException("required audit/commit unavailable");
        if (operation is "create") { await composer.Create(); }
        else { await composer.Submit(); }
        composer.Body.Should().NotBeEmpty();
        composer.CanSubmit.Should().BeFalse();
        f.Count("conversation_messages").Should().Be(operation is "terminal" ? 1 : 0);
        checkpoint.Commit = null;
        var attempts = f.Count("host_tasks");
        await composer.Inspect();
        f.Count("host_tasks").Should().Be(attempts);
        if (operation is "terminal") { composer.Status.Should().Contain("IntentRecorded"); }
        await composer.Create();
        await composer.Submit();
        f.Count("host_tasks").Should().Be(attempts);
    }

    [Fact]
    public async Task KnownEmptyCreationWithUnconfirmedTerminalReceiptStaysDisclosedAndNeverCreatesOrSubmitsAgain()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var tasks = new FailedCreationTerminal(f.Tasks);
        var workspace = new SessionWorkspaceService(f.Store, new(tasks), composed.Access, NullLogger<SessionWorkspaceService>.Instance);
        using var composer = new NewRequestComposer(workspace, composed.Conversation, composed.Access, NullLogger<NewRequestComposer>.Instance);
        composer.Body = "recoverable original";
        await composer.Create();
        var id = composer.Creation!.Request.SessionId;
        (await f.Store.ReadMetadataAsync(id, f.Token)).Metadata!.Name.Value.Should().Be("New conversation");
        await composer.Inspect();
        composer.Target!.Authority.SessionId.Should().Be(id);
        composer.Status.Should().Contain("IntentRecorded");
        composer.Body.Should().Be("recoverable original");
        var count = f.Count("work_sessions");
        composer.Review();
        await composer.Create();
        await composer.Submit();
        f.Count("work_sessions").Should().Be(count);
        f.Count("conversation_messages").Should().Be(0);
    }

    [Fact]
    public async Task DetectedNewRequestCandidateUsesRequiredContentFreeRejectionAuditAndOnlyFreshCorrectedReviewMaySubmit()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        using var composer = composed.NewComposer();
        await composer.Create();
        const string candidate = "api_key=synthetic-only-value";
        composer.Body = candidate;
        composer.Review();
        await composer.Submit();
        composer.Body.Should().Be(candidate);
        composer.CanSubmit.Should().BeFalse();
        composer.Submission.Should().BeNull();
        f.Count("conversation_messages").Should().Be(0);
        f.Count("conversation_drafts").Should().Be(0);
        using (var connection = f.OpenRaw())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT group_concat(envelope) FROM security_audit_events;";
            ((string)command.ExecuteScalar()!).Should().Contain("conversation.credential-rejected")
                .And.Contain("credential-like-content").And.NotContain(candidate);
        }
        composer.Body = " corrected ordinary native message\r\n🙂 ";
        await composer.Submit();
        f.Count("conversation_messages").Should().Be(0);
        composer.Review();
        await composer.Submit();
        (await f.Store.ReadConversationPageAsync(composer.Target!.Authority.SessionId, null, 25, f.Token))
            .Messages.Single().Body.Should().Be(" corrected ordinary native message\r\n🙂 ");
    }

    private sealed class FailedCreationTerminal(IHostTaskStore original) : IHostTaskStore
    {
        public ValueTask CommitAsync(HostTaskRecord record, long expectedRevision, CancellationToken token) =>
            record.State == HostTaskState.Succeeded ? ValueTask.FromException(new IOException("terminal receipt unavailable"))
                : original.CommitAsync(record, expectedRevision, token);
        public ValueTask<IReadOnlyList<HostTaskRecord>> ReadIncompleteAsync(int limit, CancellationToken token) => original.ReadIncompleteAsync(limit, token);
    }

    private sealed class NewRequestCheckpoint : IHostInteractionTransactionCheckpoint
    {
        internal string? Action { get; set; }
        internal Action? Commit { get; set; }
        internal int Calls { get; private set; }
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (Commit is null) { return; }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT json_extract(envelope,'$.Audit.ActionId') FROM security_audit_events ORDER BY sequence DESC LIMIT 1;";
            if (!string.Equals((string?)command.ExecuteScalar(), Action, StringComparison.Ordinal)) { return; }
            Calls++;
            Commit();
        }
    }
}
