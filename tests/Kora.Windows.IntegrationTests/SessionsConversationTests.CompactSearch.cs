using Avalonia.Controls;
using Avalonia.Input;

using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CompactSearchSharesQueryPagesKeyboardAndSavedBaselineWithoutReadingOnOpenOrTyping(bool done, bool compactFirst)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            for (var index = 0; index < 26; index++)
            { await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker original " + index); }
            await WindowsSqliteConversationTests.Save(f, await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token), "saved baseline");
            await using var composed = new Composition(f);
            if (done)
            {
                await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
            }
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show();
            compact.Show();
            try
            {
                model.SelectedSessionRecord.Should().BeNull();
                model.ConversationSearchMessages.Should().BeEmpty();
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                await model.RecoverLegacyUnsentAsync();
                model.ComposerDirty.Should().BeTrue();
                model.ComposerBody = "dirty independent";
                var before = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var field = compact.FindControl<TextBox>("ConversationQuery")!;
                field.Text = "marker";
                workspace.FindControl<TextBox>("ConversationQuery")!.Text.Should().Be("marker");
                model.ConversationSearchMessages.Should().BeEmpty();
                Click(compact, "SearchConversation");
                await compact.ConversationOperation;
                model.ConversationSearchMessages.Should().HaveCount(25);
                workspace.FindControl<ListBox>("ConversationSearchResults")!.ItemsSource!.Cast<ConversationMessage>()
                    .Should().Equal(model.ConversationSearchMessages);
                compact.FindControl<ListBox>("ConversationSearchResults")!.ItemsSource!.Cast<ConversationMessage>()
                    .Should().Equal(model.ConversationSearchMessages);
                model.CanNextConversationSearch.Should().BeTrue();
                workspace.FindControl<Button>("NextConversationSearch")!.IsEnabled.Should().BeTrue();
                Click(workspace, "NextConversationSearch");
                await workspace.ConversationOperation;
                model.ConversationSearchMessages.Single().Sequence.Should().Be(26);
                model.CanNextConversationSearch.Should().BeFalse();
                model.ConversationSearchStatus.Should().Contain("End of original sequence snapshot");
                model.ComposerBody.Should().Be("dirty independent");
                model.ComposerDirty.Should().BeTrue();
                model.CanSubmitConversation.Should().Be(!done);
                field.Focus();
                var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                field.RaiseEvent(escape);
                escape.Handled.Should().BeTrue();
                field.IsFocused.Should().BeTrue();
                compact.IsVisible.Should().BeTrue();
                workspace.IsVisible.Should().BeTrue();
                model.ConversationQuery.Should().BeEmpty();
                model.ConversationSearchMessages.Should().BeEmpty();
                workspace.FindControl<TextBox>("ConversationQuery")!.Text = "Unavailable";
                field.Text.Should().Be("Unavailable");
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await compact.ConversationOperation;
                model.ConversationSearchMessages.Should().HaveCount(25);
                Click(workspace, "ClearConversationSearch");
                model.ConversationQuery.Should().BeEmpty();
                field.Text = "marker";
                workspace.FindControl<TextBox>("ConversationQuery")!.RaiseEvent(
                    new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await workspace.ConversationOperation;
                var first = compactFirst ? (Window)compact : workspace;
                var survivor = compactFirst ? (Window)workspace : compact;
                first.Close();
                model.ConversationSearchMessages.Should().HaveCount(25);
                model.ConversationQuery.Should().Be("marker");
                Click(survivor, "ClearConversationSearch");
                model.ComposerBody = "saved baseline";
                model.ComposerDirty.Should().BeTrue("nonempty unsent text is never treated as saved");
                (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Body.Should().Be("saved baseline");
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(before);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("lifecycle")]
    [InlineData("metadata")]
    public async Task CompletedSharedSearchClearsOnObservedPrivateOrExactSourceRevocation(string revoke)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker original");
            await using var composed = new Composition(f);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show();
            compact.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ConversationQuery = "marker";
                await model.SearchConversationAsync();
                model.ConversationSearchMessages.Should().ContainSingle();
                switch (revoke)
                {
                    case "private": composed.Access.CanInspect = false; break;
                    case "control": composed.Access.ControlRevision++; break;
                    case "lifecycle":
                        await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token);
                        break;
                    case "metadata":
                        await composed.Workspace.RenameAsync(f.Request.SessionId, new(1), 0, new SessionName("changed"), RequestOrigin.LocalUi, f.Token);
                        break;
                }
                await model.RefreshWorkAsync();
                model.ConversationQuery.Should().BeEmpty(model.Status);
                model.ConversationSearchMessages.Should().BeEmpty();
                workspace.FindControl<TextBox>("ConversationQuery")!.Text.Should().BeNullOrEmpty();
                compact.FindControl<TextBox>("ConversationQuery")!.Text.Should().BeNullOrEmpty();
                workspace.FindControl<ListBox>("ConversationSearchResults")!.ItemCount.Should().Be(0);
                compact.FindControl<ListBox>("ConversationSearchResults")!.ItemCount.Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("escape")]
    [InlineData("edit")]
    [InlineData("role")]
    [InlineData("outcome")]
    [InlineData("reason")]
    [InlineData("task-state")]
    [InlineData("minimum-sequence")]
    [InlineData("maximum-sequence")]
    [InlineData("minimum-generation")]
    [InlineData("maximum-generation")]
    [InlineData("minimum-revision")]
    [InlineData("maximum-revision")]
    [InlineData("submitted-from")]
    [InlineData("submitted-through")]
    [InlineData("request-id")]
    [InlineData("task-id")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("owner")]
    [InlineData("selection")]
    [InlineData("lifecycle")]
    public async Task SharedSearchFencesDelayedReaderAfterCancellationOrExactPrivateSourceLoss(string revoke)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await WindowsSqliteConversationTests.Submit(f, f.Request.SessionId, "marker original");
            var original = f.Request.SessionId;
            var snapshot = await f.Store.ReadConversationPageAsync(original, null, 50, f.Token);
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var release = new TaskCompletionSource<ConversationPage>();
            CancellationToken observed = default;
            var reader = new SearchReader((session, cursor, limit, token) =>
                {
                    session.Should().Be(original);
                    cursor.Should().BeNull();
                    limit.Should().Be(50);
                    observed = token;
                    return new ValueTask<ConversationPage>(release.Task);
                });
            await using var composed = new Composition(f, conversationReader: reader);
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model);
            var workspace = new SessionsWindow(model, owner);
            var compact = new CompactSessionWindow(model, owner);
            workspace.Show();
            compact.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
                model.ComposerBody = "dirty independent";
                compact.FindControl<TextBox>("ConversationQuery")!.Text = "marker";
                model.ConversationOutcome = ModelTurnOutcome.Unavailable;
                model.ConversationReason = ModelTurnReason.UnregisteredModel;
                model.ConversationTaskState = HostTaskState.Unavailable;
                model.ConversationMinimumSequence = "1";
                model.ConversationMaximumSequence = "100";
                model.ConversationMinimumGeneration = "1";
                model.ConversationMaximumGeneration = "9223372036854775807";
                model.ConversationMinimumRevision = "1";
                model.ConversationMaximumRevision = "2";
                model.ConversationSubmittedFrom = "0001-01-01T00:00:00.0000000+00:00";
                model.ConversationSubmittedThrough = "9999-12-31T23:59:59.9999999+00:00";
                model.ConversationRequestId = snapshot.Messages.Single().Request.RequestId.Value.ToString("D");
                model.ConversationTaskId = snapshot.Messages.Single().Request.TaskId.Value.ToString("D");
                reader.Reads.Should().Be(0, "opening and typing never read bodies");
                Click(compact, "SearchConversation");
                var pending = compact.ConversationOperation;
                pending.IsCompleted.Should().BeFalse();
                workspace.FindControl<Button>("CancelConversationSearch")!.IsEnabled.Should().BeTrue();
                switch (revoke)
                {
                    case "cancel": Click(workspace, "CancelConversationSearch"); break;
                    case "escape": compact.FindControl<TextBox>("ConversationQuery")!.RaiseEvent(
                        new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); break;
                    case "edit": workspace.FindControl<TextBox>("ConversationQuery")!.Text = "new query"; break;
                    case "role": workspace.FindControl<ComboBox>("ConversationRole")!.SelectedItem = ConversationSearchRole.HOST; break;
                    case "outcome": model.ConversationOutcome = ModelTurnOutcome.Denied; break;
                    case "reason": model.ConversationReason = ModelTurnReason.CallerCancelled; break;
                    case "task-state": model.ConversationTaskState = HostTaskState.Cancelled; break;
                    case "minimum-sequence": model.ConversationMinimumSequence = "2"; break;
                    case "maximum-sequence": model.ConversationMaximumSequence = "99"; break;
                    case "minimum-generation": model.ConversationMinimumGeneration = "2"; break;
                    case "maximum-generation": model.ConversationMaximumGeneration = "1"; break;
                    case "minimum-revision": model.ConversationMinimumRevision = "2"; break;
                    case "maximum-revision": model.ConversationMaximumRevision = "1"; break;
                    case "submitted-from": model.ConversationSubmittedFrom = "1970-01-01T00:00:00.0000000+00:00"; break;
                    case "submitted-through": model.ConversationSubmittedThrough = "1970-01-01T00:00:00.0000000+00:00"; break;
                    case "request-id": model.ConversationRequestId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"; break;
                    case "task-id": model.ConversationTaskId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"; break;
                    case "private": composed.Access.CanInspect = false; break;
                    case "control": composed.Access.ControlRevision++; break;
                    case "owner": owner.Revoke(); break;
                    case "selection":
                        model.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Discard);
                        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
                        break;
                    case "lifecycle":
                        await composed.Workspace.ChangeLifecycleAsync(original, new(1), false, RequestOrigin.LocalUi, f.Token);
                        await model.RefreshWorkAsync();
                        break;
                }
                var rangeEdit = revoke is "minimum-sequence" or "maximum-sequence" or "submitted-from" or "submitted-through" or "request-id" or "task-id"
                    or "minimum-generation" or "maximum-generation" or "minimum-revision" or "maximum-revision";
                if (rangeEdit || revoke is "cancel" or "escape" or "edit" or "role" or "outcome" or "reason" or "task-state" or "owner" or "selection" or "lifecycle")
                { observed.IsCancellationRequested.Should().BeTrue(); }
                reader.Reads.Should().Be(1, "query/filter edits must never schedule another read");
                release.SetResult(snapshot);
                await pending;
                model.ConversationSearchMessages.Should().BeEmpty();
                model.CanNextConversationSearch.Should().BeFalse();
                model.CanCancelConversationSearch.Should().BeFalse();
                model.ConversationQuery.Should().Be(rangeEdit ? "marker" : revoke switch { "edit" => "new query", "role" or "outcome" or "reason" or "task-state" => "marker", _ => string.Empty });
                model.ConversationRole.Should().Be(revoke is "role" ? ConversationSearchRole.HOST : ConversationSearchRole.All);
                var criteriaEdit = rangeEdit || revoke is "edit" or "role" or "outcome" or "reason" or "task-state";
                model.ConversationMinimumGeneration.Should().Be(revoke is "minimum-generation" ? "2" : criteriaEdit ? "1" : string.Empty);
                model.ConversationMaximumGeneration.Should().Be(revoke is "maximum-generation" ? "1" : criteriaEdit ? "9223372036854775807" : string.Empty);
                model.ConversationMinimumRevision.Should().Be(revoke is "minimum-revision" ? "2" : criteriaEdit ? "1" : string.Empty);
                model.ConversationMaximumRevision.Should().Be(revoke is "maximum-revision" ? "1" : criteriaEdit ? "2" : string.Empty);
                if (!criteriaEdit)
                {
                    model.ConversationRequestId.Should().BeEmpty();
                    model.ConversationTaskId.Should().BeEmpty();
                }
                else
                {
                    model.ConversationRequestId.Should().Be(revoke is "request-id" ? "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                        : snapshot.Messages.Single().Request.RequestId.Value.ToString("D"));
                    model.ConversationTaskId.Should().Be(revoke is "task-id" ? "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
                        : snapshot.Messages.Single().Request.TaskId.Value.ToString("D"));
                }
                model.ConversationMinimumSequence.Should().Be(revoke is "minimum-sequence" ? "2" : criteriaEdit ? "1" : string.Empty);
                model.ConversationMaximumSequence.Should().Be(revoke is "maximum-sequence" ? "99" : criteriaEdit ? "100" : string.Empty);
                model.ConversationSubmittedFrom.Should().Be(revoke is "submitted-from" ? "1970-01-01T00:00:00.0000000+00:00"
                    : criteriaEdit ? "0001-01-01T00:00:00.0000000+00:00" : string.Empty);
                model.ConversationSubmittedThrough.Should().Be(criteriaEdit
                    ? revoke is "submitted-through" ? "1970-01-01T00:00:00.0000000+00:00" : "9999-12-31T23:59:59.9999999+00:00" : string.Empty);
                model.ConversationOutcome.Should().Be(revoke is "outcome" ? ModelTurnOutcome.Denied
                    : criteriaEdit ? ModelTurnOutcome.Unavailable : null);
                model.ConversationReason.Should().Be(revoke is "reason" ? ModelTurnReason.CallerCancelled
                    : criteriaEdit ? ModelTurnReason.UnregisteredModel : null);
                model.ConversationTaskState.Should().Be(revoke is "task-state" ? HostTaskState.Cancelled
                    : criteriaEdit ? HostTaskState.Unavailable : null);
                workspace.FindControl<ComboBox>("ConversationOutcome")!.SelectedItem.Should().Be(model.ConversationOutcomeChoice);
                compact.FindControl<ComboBox>("ConversationOutcome")!.SelectedItem.Should().Be(model.ConversationOutcomeChoice);
                workspace.FindControl<ComboBox>("ConversationReason")!.SelectedItem.Should().Be(model.ConversationReasonChoice);
                compact.FindControl<ComboBox>("ConversationReason")!.SelectedItem.Should().Be(model.ConversationReasonChoice);
                workspace.FindControl<ComboBox>("ConversationTaskState")!.SelectedItem.Should().Be(model.ConversationTaskStateChoice);
                compact.FindControl<ComboBox>("ConversationTaskState")!.SelectedItem.Should().Be(model.ConversationTaskStateChoice);
                workspace.FindControl<ComboBox>("ConversationRole")!.SelectedItem.Should().Be(model.ConversationRole);
                compact.FindControl<ComboBox>("ConversationRole")!.SelectedItem.Should().Be(model.ConversationRole);
                workspace.FindControl<TextBox>("ConversationQuery")!.Text.Should().Be(model.ConversationQuery);
                compact.FindControl<TextBox>("ConversationQuery")!.Text.Should().Be(model.ConversationQuery);
                if (revoke is "cancel" or "escape" or "edit" or "role" or "lifecycle")
                { model.ComposerBody.Should().Be("dirty independent"); }
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedSearchRevokesQueryOnUnavailableOrChangedConversationSource(bool snapshotChanged)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var reader = new SearchReader((_, _, _, _) =>
            throw (snapshotChanged ? new ConversationSnapshotChangedException() : new IOException("private unavailable marker")));
        await using var composed = new Composition(f, conversationReader: reader);
        await composed.Model.RefreshAsync();
        await composed.Model.SelectAsync(composed.Model.Sessions.Single());
        composed.Model.ComposerBody = "dirty independent";
        composed.Model.ConversationQuery = "marker";
        composed.Model.ConversationOutcome = ModelTurnOutcome.Unavailable;
        composed.Model.ConversationReason = ModelTurnReason.UnregisteredModel;
        composed.Model.ConversationTaskState = HostTaskState.Unavailable;
        await composed.Model.SearchConversationAsync();
        composed.Model.ConversationSearchMessages.Should().BeEmpty();
        composed.Model.ConversationQuery.Should().BeEmpty();
        composed.Model.ConversationOutcome.Should().BeNull();
        composed.Model.ConversationReason.Should().BeNull();
        composed.Model.ConversationTaskState.Should().BeNull();
        composed.Model.ConversationSearchStatus.Should().Contain(snapshotChanged ? "Snapshot changed" : "unavailable");
        composed.Model.ComposerBody.Should().Be("dirty independent");
    }

    private sealed class SearchReader(
        Func<HostId<SessionIdentity>, ConversationCursor?, int, CancellationToken, ValueTask<ConversationPage>> read,
        Func<HostId<SessionIdentity>, CancellationToken, ValueTask<ConversationDraftSnapshot>>? readDraft = null,
        Func<HostId<SessionIdentity>, CancellationToken, ValueTask<SessionWorkspaceEntry>>? readMetadata = null)
        : ISessionWorkspaceStore, ISessionConversationStore
    {
        internal int Reads { get; private set; }
        internal int DraftReads { get; private set; }
        internal bool DenyReads { get; set; }
        internal bool DenyDraftReads { get; set; }
        internal Func<Task>? AfterDraftRead { get; set; }
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session, ConversationCursor? cursor,
            int limit, CancellationToken token)
        {
            Reads++;
            if (DenyReads) { throw new IOException("Synthetic source-read denial."); }
            return read(session, cursor, limit, token);
        }

        public async ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            DraftReads++;
            if (DenyReads || DenyDraftReads) { throw new IOException("Synthetic draft-read denial."); }
            var result = await (readDraft ?? throw new NotSupportedException())(session, token);
            if (AfterDraftRead is { } after) { await after(); }
            return result;
        }
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session, HostId<RequestIdentity> request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request, HostRevision generation, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control, ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request, HostRevision generation, long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request, ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session, HostRevision expectedGeneration, long expectedMetadataRevision, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview, Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken) =>
            (readMetadata ?? throw new NotSupportedException())(session, cancellationToken);
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target, Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name, Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision expectedGeneration, long expectedMetadataRevision, SessionName name, Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision expectedGeneration, bool active, Func<bool> canControl, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
