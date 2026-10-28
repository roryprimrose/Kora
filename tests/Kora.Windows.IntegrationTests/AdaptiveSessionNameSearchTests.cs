using System.ComponentModel;

using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class AdaptiveSessionNameSearchTests
{
    [Fact]
    public async Task NativeNameSearchSelectsAndContinuesOnlyTheExactConversationWithIndependentUnsentBuffers()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var model = f.Model;
            var field = f.View.FindControl<TextBox>("SessionNameQuery")!;
            var search = f.View.FindControl<Button>("SearchSessionNames")!;
            field.Focus().Should().BeTrue();
            ControlAutomationPeer.CreatePeerForElement(field)!.GetName().Should().Contain("editing does not search");
            search.IsEnabled.Should().BeFalse();
            model.SelectedSessionRecord.Should().BeNull();

            // Advanced management's technical mode must not leak into ordinary name search.
            model.ListSearchKind = SessionListSearchKind.ExactId;
            model.ListNameAvailability = SessionNameAvailability.Unnamed;
            field.Text = "café 100%_";
            Dispatcher.UIThread.RunJobs();
            field.Text.Should().Be("café 100%_");
            search.IsEnabled.Should().BeTrue();
            model.ListSearchKind.Should().Be(SessionListSearchKind.NameSubstring);
            model.ListNameAvailability.Should().Be(SessionNameAvailability.Any);
            f.Probe.ContentReads.Should().Be(0);
            await f.Click("SearchSessionNames");
            model.Sessions.Select(row => row.Authority.SessionId).Should()
                .BeEquivalentTo([f.First.Authority.SessionId, f.Second.Authority.SessionId]);
            model.SelectedSessionRecord.Should().BeNull("duplicate matching names never choose a conversation");
            model.ConversationMessages.Should().BeEmpty();
            f.Probe.ContentReads.Should().Be(0);
            f.Admission.Calls.Should().Be(0);
            var rows = model.Sessions.ToArray();
            f.View.FindControl<ListBox>("SessionList")!.SelectedItem = rows[0];
            await f.View.Operation;
            model.SelectedSessionRecord.Should().Be(rows[0]);
            model.ComposerBody = "first unsent";
            f.View.FindControl<ListBox>("SessionList")!.SelectedItem = rows[1];
            await f.View.Operation;
            model.ComposerBody.Should().BeEmpty();
            model.ComposerBody = "second unsent";
            f.Probe.ContentReads.Should().Be(0, "native selection reads work metadata, never conversation bodies");

            field.Text = "unrelated";
            await f.Click("SearchSessionNames");
            model.SelectedSessionRecord.Should().BeNull();
            model.ComposerBody.Should().BeEmpty();
            field.Text = "café 100%_";
            var enter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
            field.RaiseEvent(enter);
            await f.View.Operation;
            enter.Handled.Should().BeTrue();
            await f.Select(rows[0].Authority.SessionId);
            model.ComposerBody.Should().Be("first unsent");
            await f.Select(rows[1].Authority.SessionId);
            model.ComposerBody.Should().Be("second unsent");
            f.Probe.ContentReads.Should().Be(0);
            (await f.Native.Interactions.ReadConversationDraftAsync(rows[0].Authority.SessionId, f.Native.Token))
                .Draft.Body.Should().BeNull();
            (await f.Native.Interactions.ReadConversationDraftAsync(rows[1].Authority.SessionId, f.Native.Token))
                .Draft.Body.Should().BeNull();

            await f.Click("SubmitMessage");
            f.Admission.Calls.Should().Be(1);
            model.ConversationMessages.Should().ContainSingle().Which.Request.SessionId
                .Should().Be(rows[1].Authority.SessionId);
            model.ConversationMessages.Single().Body.Should().Be("second unsent");
            (await f.Native.Interactions.ReadConversationPageAsync(rows[0].Authority.SessionId, null, 25, f.Native.Token))
                .Messages.Should().BeEmpty();
            await f.Select(rows[0].Authority.SessionId);
            model.ComposerBody.Should().Be("first unsent", "continuing another conversation cannot transfer its buffer");
        });
    }

    [Fact]
    public async Task ExplicitNativeSearchAndNextKeepPagesBoundedAndClearReturnsToPassiveBrowsing()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            for (var index = 0; index < 27; index++)
            {
                await f.Native.Sessions.CreateAsync(new("Paging topic " + index), RequestOrigin.LocalUi, f.Native.Token);
            }
            var model = f.Model;
            var original = model.Sessions.ToArray();
            f.Field.Text = "Paging topic";
            model.Sessions.Should().Equal(original, "editing an ordinary list does not read a new page");
            await f.Click("SearchSessionNames");
            model.Sessions.Should().HaveCount(25);
            var firstPage = model.Sessions.Select(row => row.Authority.SessionId).ToArray();
            model.SelectedSessionRecord.Should().BeNull();
            f.View.FindControl<Button>("NextSessionNameSearch")!.IsEnabled.Should().BeTrue();
            f.View.FindControl<Button>("NextSessions")!.IsEnabled.Should().BeFalse();
            await f.Run("select the first session");
            var captured = f.Sessions.CaptureSessionSelection(1, static () => true);
            await f.WhenAsync(() => model.CanNextSessionNameSearch);
            await f.Click("NextSessionNameSearch");
            model.Sessions.Should().HaveCount(2);
            model.Sessions.Should().NotContain(row => firstPage.Contains(row.Authority.SessionId));
            model.SelectedSessionRecord.Should().BeNull();
            await captured();
            model.SelectedSessionRecord.Should().BeNull("an old ordinal must not select the replacement page");
            model.CanNextSessionNameSearch.Should().BeFalse();
            await f.Click("ClearSessionNameSearch");
            f.Field.IsFocused.Should().BeTrue();
            f.Field.Text.Should().BeEmpty();
            model.Sessions.Should().BeEmpty();
            model.CanNextSessionNameSearch.Should().BeFalse();
            await f.Click("RefreshSessions");
            model.Sessions.Should().NotBeEmpty();
            model.SelectedSessionRecord.Should().BeNull();
            f.Probe.ContentReads.Should().Be(0);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(" café")]
    [InlineData("café ")]
    [InlineData("cafe\u0301")]
    [InlineData("bad\nquery")]
    [InlineData("bad\u200bquery")]
    [InlineData("oversized")]
    public async Task InvalidNativeQueryStaysExactAndCannotSearch(string query)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            f.Field.Text = "café";
            await f.Click("SearchSessionNames");
            await f.Run("select the first session");
            var captured = f.Sessions.CaptureSessionSelection(1, static () => true);
            if (string.Equals(query, "oversized", StringComparison.Ordinal)) { query = new('x', 121); }
            f.Field.Text = query;
            Dispatcher.UIThread.RunJobs();
            f.Field.Text.Should().Be(query);
            f.Model.SessionNameQuery.Should().Be(query);
            f.View.FindControl<Button>("SearchSessionNames")!.IsEnabled.Should().BeFalse();
            f.View.FindControl<Button>("NextSessionNameSearch")!.IsEnabled.Should().BeFalse();
            f.Model.Sessions.Should().BeEmpty();
            f.Model.SelectedSessionRecord.Should().BeNull();
            await captured();
            f.Model.SelectedSessionRecord.Should().BeNull();
            var enter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter };
            f.Field.RaiseEvent(enter);
            enter.Handled.Should().BeFalse();
            var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
            f.Field.RaiseEvent(escape);
            escape.Handled.Should().BeTrue();
            f.Field.Text.Should().BeEmpty();
            f.Window.IsVisible.Should().BeTrue("Escape clears only the search");
            f.Probe.ContentReads.Should().Be(0);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Fact]
    public async Task NameMatchingIsOrdinalAndDoneSelectionNeverResumes()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            f.Field.Text = "CAFÉ";
            await f.Click("SearchSessionNames");
            f.Model.Sessions.Should().BeEmpty();
            f.Model.SessionFilter = SessionListFilter.Done;
            f.Field.Text = "Completed";
            await f.Click("SearchSessionNames");
            f.Model.Sessions.Should().ContainSingle().Which.Authority.SessionId.Should().Be(f.Done.Authority.SessionId);
            await f.Run("select the first session");
            f.Model.SelectedSessionRecord!.Authority.Should().Be(f.Done.Authority);
            f.Model.CanCompose.Should().BeFalse();
            (await f.Native.Interactions.ReadMetadataAsync(f.Done.Authority.SessionId, f.Native.Token))
                .Authority.Should().Be(f.Done.Authority);
            f.Probe.ContentReads.Should().Be(0);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("search")]
    [InlineData("clear")]
    [InlineData("source")]
    [InlineData("privacy")]
    public async Task CapturedVoiceOrdinalRetiresWithTheNameSearchSource(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            f.Field.Text = "café";
            await f.Click("SearchSessionNames");
            var captured = f.Sessions.CaptureSessionSelection(1, static () => true);
            switch (boundary)
            {
                case "edit": f.Field.Text = "unrelated"; break;
                case "search": await f.Click("SearchSessionNames"); break;
                case "clear": await f.Click("ClearSessionNameSearch"); break;
                case "source": f.Model.RevokeSessionList(); break;
                case "privacy":
                    await f.Run("select the first session");
                    f.Model.ComposerBody = "private unsent";
                    f.Native.Main.CloseForObservedPrivacyEvent("Synthetic name search privacy closure", hidePresentation: true);
                    await f.Native.Main.PrivacyClosureTask;
                    f.Model.SessionNameQuery.Should().BeEmpty();
                    f.Model.ComposerBody.Should().BeEmpty();
                    f.Model.HasUnsentMessages.Should().BeFalse();
                    f.Model.Sessions.Should().BeEmpty();
                    f.Model.CanSearchSessionNames.Should().BeFalse();
                    break;
            }
            await captured();
            f.Model.SelectedSessionRecord.Should().BeNull();
            f.Model.ConversationMessages.Should().BeEmpty();
            f.Probe.ContentReads.Should().Be(0);
            f.Admission.Calls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("edit")]
    [InlineData("source")]
    [InlineData("privacy")]
    public async Task CancelOrRevokeDuringNativeSearchSuppressesLateMetadataUntilTheReadJoins(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var probe = new ConversationProbe(f.Native.Interactions) { HoldMetadata = true };
            var service = new SessionWorkspaceService(probe, new(f.Native.Tasks), f.Native.Access,
                NullLogger<SessionWorkspaceService>.Instance);
            var model = new SessionsViewModel(service, f.Native.Evidence, f.Native.Access,
                NullLogger<SessionsViewModel>.Instance);
            var view = new SessionInteractionView { DataContext = model };
            var window = new Window { Content = view };
            window.Show();
            try
            {
                var field = view.FindControl<TextBox>("SessionNameQuery")!;
                field.Text = "café";
                probe.MetadataReads.Should().Be(0, "typing must not read metadata");
                view.FindControl<Button>("SearchSessionNames")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await probe.MetadataStarted.Task;
                view.Operation.IsCompleted.Should().BeFalse();
                view.FindControl<Button>("CancelSessionNameSearch")!.IsEnabled.Should().BeTrue();
                switch (boundary)
                {
                    case "cancel":
                        view.FindControl<Button>("CancelSessionNameSearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                    case "edit": field.Text = "unrelated"; break;
                    case "source": model.RevokeSessionList(); break;
                    case "privacy": model.Close(); break;
                }
                probe.ReadToken.IsCancellationRequested.Should().BeTrue();
                view.Operation.IsCompleted.Should().BeFalse("cancellation is not physical read completion");
                probe.MetadataCompletion.SetResult(new([f.First], null));
                await view.Operation;
                model.Sessions.Should().BeEmpty();
                model.SelectedSessionRecord.Should().BeNull();
                model.CanCancelListSearch.Should().BeFalse();
                model.CanNextSessionNameSearch.Should().BeFalse();
                probe.MetadataReads.Should().Be(1);
                probe.ContentReads.Should().Be(0);
                if (!string.Equals(boundary, "privacy", StringComparison.Ordinal)) { model.Status.Should().Contain("Cancelled"); }
            }
            finally
            {
                probe.MetadataCompletion.TrySetResult(new([], null));
                await view.Operation;
                model.Close();
                window.Close();
            }
        });
    }

    [Fact]
    public async Task NativeNextCanContinueAfterAValidatedPageWithZeroNameMatches()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            await f.Initialize();
            var nonmatch = new SessionWorkspaceEntry(new(new(new Guid("00000000-0000-0000-0000-000000000001")),
                new(1), true), null);
            var match = new SessionWorkspaceEntry(new(new(new Guid("00000000-0000-0000-0000-000000000002")),
                new(1), true), new(new(new Guid("00000000-0000-0000-0000-000000000002")), new(1), new("needle")));
            var probe = new ConversationProbe(f.Native.Interactions);
            probe.MetadataPages.Enqueue(new([nonmatch], nonmatch.Authority.SessionId.Value));
            probe.MetadataPages.Enqueue(new([match], null));
            var service = new SessionWorkspaceService(probe, new(f.Native.Tasks), f.Native.Access,
                NullLogger<SessionWorkspaceService>.Instance);
            var model = new SessionsViewModel(service, f.Native.Evidence, f.Native.Access,
                NullLogger<SessionsViewModel>.Instance);
            var view = new SessionInteractionView { DataContext = model };
            var window = new Window { Content = view };
            window.Show();
            try
            {
                view.FindControl<TextBox>("SessionNameQuery")!.Text = "needle";
                probe.MetadataReads.Should().Be(0);
                view.FindControl<Button>("SearchSessionNames")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.Operation;
                model.Sessions.Should().BeEmpty();
                model.Status.Should().Contain("0 matches").And.Contain("Next metadata search continues even after zero matches");
                view.FindControl<Button>("NextSessionNameSearch")!.IsEnabled.Should().BeTrue();
                view.FindControl<Button>("NextSessionNameSearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.Operation;
                model.Sessions.Should().ContainSingle().Which.Should().Be(match);
                model.SelectedSessionRecord.Should().BeNull();
                model.CanNextSessionNameSearch.Should().BeFalse();
                probe.MetadataReads.Should().Be(2);
                probe.MetadataAfter.Should().Be(nonmatch.Authority.SessionId.Value);
                probe.MetadataLimit.Should().Be(SessionListSearchPage.MaximumScannedRecords);
                probe.ContentReads.Should().Be(0);
            }
            finally { model.Close(); window.Close(); }
        });
    }

    private sealed class Fixture : IDisposable
    {
        internal NativeUxFixtureSession Native { get; } = new(Path.GetFullPath("."), silentCaption: true);
        internal Admission Admission { get; } = new();
        internal ConversationProbe Probe { get; private set; } = null!;
        internal SessionsWindowController Sessions { get; private set; } = null!;
        private ResponseWindowController? responses;
        private AllWorkWindowController? all;
        private WorkspaceSurfaceCommandController? commands;
        internal ResponseWindow Window { get; private set; } = null!;
        internal SessionInteractionView View => Window.SessionView;
        internal TextBox Field => View.FindControl<TextBox>("SessionNameQuery")!;
        internal SessionsViewModel Model => (SessionsViewModel)View.DataContext!;
        internal SessionWorkspaceEntry First { get; private set; } = null!;
        internal SessionWorkspaceEntry Second { get; private set; } = null!;
        internal SessionWorkspaceEntry Done { get; private set; } = null!;

        internal async Task Initialize()
        {
            await Native.InitializeAsync();
            First = await Native.Sessions.CreateAsync(new("café 100%_"), RequestOrigin.LocalUi, Native.Token);
            Second = await Native.Sessions.CreateAsync(new("café 100%_"), RequestOrigin.LocalUi, Native.Token);
            await Native.Sessions.CreateAsync(new("unrelated"), RequestOrigin.LocalUi, Native.Token);
            var done = await Native.Sessions.CreateAsync(new("Completed café"), RequestOrigin.LocalUi, Native.Token);
            var authority = await Native.Sessions.ChangeLifecycleAsync(done.Authority.SessionId, done.Authority.Generation,
                false, RequestOrigin.LocalUi, Native.Token);
            Done = done with { Authority = authority };
            Probe = new(Native.Interactions);
            var reading = new SessionWorkspaceService(Probe, new(Native.Tasks), Native.Access,
                NullLogger<SessionWorkspaceService>.Instance);
            var conversation = new SessionConversationService(Native.Interactions, Native.Interactions, reading,
                Native.Access, Admission, TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            responses = new(Native.Main, NullLogger<ResponseWindowController>.Instance);
            Sessions = new(Native.Main, Native.Sessions, Native.Evidence, Native.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            Sessions.CreateInteraction = (model, owner) =>
                Window = responses.PrepareSessions(model, owner, Sessions.Open, static () => { });
            Sessions.Bind();
            all = new(Native.Main, Native.Sessions, Native.Access, NullLogger<AllWorkViewModel>.Instance, Sessions);
            commands = new(Native.Main, Native.Access, all, Sessions);
            commands.Bind();
            await Run("show sessions");
        }

        internal async Task Click(string name)
        {
            View.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await View.Operation;
            Dispatcher.UIThread.RunJobs();
        }

        internal async Task WhenAsync(Func<bool> condition)
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(object? sender, PropertyChangedEventArgs args) { if (condition()) { signal.TrySetResult(); } }
            Model.PropertyChanged += Changed;
            try { if (!condition()) { await signal.Task.WaitAsync(Native.Token); } }
            finally { Model.PropertyChanged -= Changed; }
        }

        internal async Task Select(HostId<SessionIdentity> session)
        {
            View.FindControl<ListBox>("SessionList")!.SelectedItem = Model.Sessions.Single(row => row.Authority.SessionId == session);
            await View.Operation;
        }

        internal async Task Run(string phrase)
        {
            Native.Main.CommandText = phrase;
            await Native.Main.RunTypedCommand.ExecuteAsync();
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            commands?.Dispose();
            all?.Dispose();
            Sessions?.Dispose();
            responses?.Dispose();
            Native.Dispose();
        }
    }

    private sealed class Admission : IModelTurnAdmission
    {
        internal int Calls { get; private set; }
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new ModelTurnAdmission(new(ModelTurnOutcome.Unavailable, ModelTurnReason.GenerationNotEnabled)));
        }
    }

    private sealed class ConversationProbe(WindowsSqliteHostInteractionStore store) : ISessionWorkspaceStore, ISessionConversationStore
    {
        internal int ContentReads { get; private set; }
        internal int MetadataReads { get; private set; }
        internal bool HoldMetadata { get; init; }
        internal Queue<SessionPage<SessionWorkspaceEntry>> MetadataPages { get; } = new();
        internal Guid? MetadataAfter { get; private set; }
        internal int MetadataLimit { get; private set; }
        internal CancellationToken ReadToken { get; private set; }
        internal TaskCompletionSource MetadataStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<SessionPage<SessionWorkspaceEntry>> MetadataCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken token)
        {
            MetadataReads++;
            MetadataAfter = after;
            MetadataLimit = limit;
            ReadToken = token;
            MetadataStarted.TrySetResult();
            if (MetadataPages.TryDequeue(out var page)) { return ValueTask.FromResult(page); }
            return HoldMetadata ? new(MetadataCompletion.Task) : store.ReadMetadataPageAsync(after, limit, token);
        }
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken token) =>
            store.ReadMetadataAsync(session, token);
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session,
            ConversationCursor? cursor, int limit, CancellationToken token)
        {
            ContentReads++;
            return store.ReadConversationPageAsync(session, cursor, limit, token);
        }
        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            ContentReads++;
            throw new InvalidOperationException("Name navigation must not read legacy text.");
        }
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session,
            HostId<RequestIdentity> request, CancellationToken token)
        {
            ContentReads++;
            throw new InvalidOperationException("Name navigation must not read message bodies.");
        }
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request,
            HostRevision generation, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control,
            ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request, HostRevision generation,
            long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request,
            ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session,
            HostRevision expectedGeneration, long expectedMetadataRevision, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview,
            Func<bool> canControl, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken token) =>
            throw new NotSupportedException();
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task,
            CancellationToken token) => throw new NotSupportedException();
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target,
            Func<bool> canControl, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name,
            Func<bool> canControl, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision expectedGeneration,
            long expectedMetadataRevision, SessionName name, Func<bool> canControl, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session,
            Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session,
            Guid? after, int limit, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision expectedGeneration,
            bool active, Func<bool> canControl, CancellationToken token) => throw new NotSupportedException();
    }
}
