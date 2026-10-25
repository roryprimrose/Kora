using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class WorkspaceSurfaceCommandControllerTests
{
    [WindowsFact]
    public async Task ExactRoutesUseExistingSingletonsAndKeepSharedDirtySelectionAndDoneAuthority()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            var doneCreated = await f.Native.Sessions.CreateAsync(new("Done navigation"), RequestOrigin.LocalUi, f.Native.Token);
            await f.Native.Sessions.ChangeLifecycleAsync(doneCreated.Authority.SessionId,
                doneCreated.Authority.Generation, false, RequestOrigin.LocalUi, f.Native.Token);
            f.Bind();
            SessionsWindow? workspace = null;
            CompactSessionWindow? compact = null;
            AllWorkWindow? all = null;
            var opens = new int[3];
            using var workspaceOpened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => workspace = view);
            using var compactOpened = Window.WindowOpenedEvent.AddClassHandler<CompactSessionWindow>((view, _) => { compact = view; opens[2]++; });
            using var allOpened = Window.WindowOpenedEvent.AddClassHandler<AllWorkWindow>((view, _) => { all = view; opens[0]++; });
            f.Sessions!.Open();
            var model = (SessionsViewModel)workspace!.DataContext!;
            await model.RefreshAsync();
            var target = model.Sessions.Single(entry => entry.Authority.SessionId == f.Native.LifecycleTarget!.SessionId);
            await model.SelectAsync(target);
            target = model.SelectedSessionRecord!;
            model.ComposerBody = "private unsaved memory only";
            var details = model.Detail;
            var authority = target.Authority;
            await f.Run("open all work");
            await f.Run("open compact conversation");
            compact!.DataContext.Should().BeSameAs(model);
            model.SelectedSessionRecord.Should().Be(target);
            model.ComposerBody.Should().Be("private unsaved memory only");
            model.Detail.Should().Be(details);
            model.ComposerDirty.Should().BeTrue();
            compact.FindControl<TextBox>("ConversationComposer")!.Text.Should().Be(model.ComposerBody);
            all!.DataContext.Should().BeOfType<AllWorkViewModel>().Which.Records.Should().BeEmpty();
            f.AllWork!.Open();
            f.Sessions.OpenCompact();
            await f.Run("open all work");
            await f.Run("open compact conversation");
            opens.Should().Equal(1, 0, 1);
            (await f.Native.Interactions.ReadMetadataAsync(authority.SessionId, f.Native.Token)).Authority.Should().Be(authority);
            model.ComposerBody.Should().Be("private unsaved memory only");
            model.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Discard);
            await model.DiscardComposerBufferAsync();
            workspace.Close();
            f.Sessions.Open();
            workspace!.DataContext.Should().BeSameAs(model);
            model.SelectedSessionRecord.Should().Be(target);
            var done = model.Sessions.First(entry => !entry.Authority.IsActive);
            await model.SelectAsync(done);
            done = model.SelectedSessionRecord!;
            var doneAuthority = done.Authority;
            await f.Run("open compact conversation");
            model.SelectedSessionRecord.Should().Be(done);
            (await f.Native.Interactions.ReadMetadataAsync(doneAuthority.SessionId, f.Native.Token)).Authority.Should().Be(doneAuthority);
        });
    }

    [WindowsFact]
    public async Task OpeningAllThreeSurfacesDoesNotReadAnyStoreOrCreateSession()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            var store = new RefusingWorkspaceStore();
            f.Bind(new(store, new(f.Native.Tasks), f.Native.Access, NullLogger<SessionWorkspaceService>.Instance));
            await f.Run("open all work");
            f.Native.Main.ResponseBody.Should().NotContain("Navigation unavailable");
            await f.Run("open compact conversation");
            f.Native.Main.ResponseBody.Should().NotContain("Navigation unavailable");
            store.Calls.Should().Be(0);
        });
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("input")]
    [InlineData("source")]
    [InlineData("closure")]
    [InlineData("dispose")]
    public async Task QueuedOpeningCannotAcquireLaterOwnershipOrSelectedSource(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            f.Bind();
            SessionsWindow? workspace = null;
            using var workspaceOpened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => workspace = view);
            f.Sessions!.Open();
            var model = (SessionsViewModel)workspace!.DataContext!;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions[0]);
            var opens = 0;
            using var compactOpened = Window.WindowOpenedEvent.AddClassHandler<CompactSessionWindow>((_, _) => opens++);
            f.Native.Main.CommandText = "open compact conversation";
            var opening = f.Native.Main.RunTypedCommand.ExecuteAsync();
            opening.IsCompleted.Should().BeFalse("native opening is deferred to the dispatcher");
            switch (boundary)
            {
                case "owner": f.Native.Access.SetOpen(false); f.Native.Access.SetOpen(true); break;
                case "input":
                    f.Native.Main.CommandText = "open saved conversation drafts";
                    break;
                case "source": await model.SelectAsync(model.Sessions[^1]); break;
                case "closure": workspace.ClosePrivate(); break;
                case "dispose": f.Commands!.Dispose(); break;
            }
            await opening;
            opens.Should().Be(0);
        });
    }

    [WindowsFact]
    public async Task OriginalDenialIsNeverQueuedAndDisposedBindingIsExplicitlyUnavailable()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NavigationFixture();
            await f.Native.InitializeAsync();
            f.Bind();
            var opens = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<AllWorkWindow>((_, _) => opens++);
            f.Native.Access.SetOpen(false);
            await f.Run("open all work");
            f.Native.Access.SetOpen(true);
            await Dispatcher.UIThread.InvokeAsync(() => { });
            opens.Should().Be(0);
            f.Commands!.Dispose();
            await f.Run("open all work");
            f.Native.Main.ResponseBody.Should().Contain("native surface route");
            opens.Should().Be(0);
        });
    }

    private sealed class NavigationFixture : IDisposable
    {
        internal NativeUxFixtureSession Native { get; }
        internal SessionsWindowController? Sessions { get; private set; }
        internal AllWorkWindowController? AllWork { get; private set; }
        internal WorkspaceSurfaceCommandController? Commands { get; private set; }

        internal NavigationFixture()
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            Native = new(parent);
        }

        internal void Bind(SessionWorkspaceService? service = null)
        {
            service ??= Native.Sessions;
            var conversation = new SessionConversationService(Native.Interactions, Native.Interactions,
                service, Native.Access, new NoAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
            Sessions = new(Native.Main, service, Native.Evidence, Native.Access, NullLogger<SessionsViewModel>.Instance,
                conversation: conversation);
            Sessions.Bind();
            AllWork = new(Native.Main, service, Native.Access, NullLogger<AllWorkViewModel>.Instance, Sessions);
            AllWork.Bind();
            Commands = new(Native.Main, Native.Access, AllWork, Sessions);
            Commands.Bind();
        }

        private sealed class NoAdmission : Kora.Application.Dependencies.IModelTurnAdmission
        {
            public bool IsQuiescent => true;
            public Task<Kora.Application.Dependencies.ModelTurnAdmission> AdmitAsync(Kora.Core.Dependencies.ModelTurnChoice choice,
                Kora.Core.Dependencies.ModelContextEnvelope context, Kora.Application.Dependencies.ModelHandoffReview? review,
                CancellationToken token) => throw new InvalidOperationException("Navigation never admits a provider.");
        }

        internal async Task Run(string phrase)
        {
            Native.Main.CommandText = phrase;
            await Native.Main.RunTypedCommand.ExecuteAsync();
        }

        public void Dispose()
        {
            Commands?.Dispose();
            AllWork?.Dispose();
            Sessions?.Dispose();
            Native.Dispose();
        }
    }

    private sealed class RefusingWorkspaceStore : ISessionWorkspaceStore, ISessionConversationStore
    {
        internal int Calls { get; private set; }
        private T Refuse<T>() { Calls++; throw new InvalidOperationException("Opening must not access storage."); }
        public ValueTask<SessionDispositionPreview> PreviewDispositionAsync(HostId<SessionIdentity> session, HostRevision expectedGeneration, long expectedMetadataRevision, CancellationToken cancellationToken) => Refuse<ValueTask<SessionDispositionPreview>>();
        public ValueTask<SessionDispositionReceipt> DisposeSessionAsync(HostRequest request, SessionDispositionPreview preview, Func<bool> canControl, CancellationToken cancellationToken) => Refuse<ValueTask<SessionDispositionReceipt>>();
        public ValueTask<HostTaskRecord> RecordControlIntentAsync(HostRequest request, CancellationToken cancellationToken) => Refuse<ValueTask<HostTaskRecord>>();
        public ValueTask<SessionPage<WorkSessionAuthorization>> ReadSessionsAsync(Guid? after, int limit, CancellationToken cancellationToken) => Refuse<ValueTask<SessionPage<WorkSessionAuthorization>>>();
        public ValueTask<SessionPage<SessionWorkspaceEntry>> ReadMetadataPageAsync(Guid? after, int limit, CancellationToken cancellationToken) => Refuse<ValueTask<SessionPage<SessionWorkspaceEntry>>>();
        public ValueTask<SessionWorkspaceEntry> ReadMetadataAsync(HostId<SessionIdentity> session, CancellationToken cancellationToken) => Refuse<ValueTask<SessionWorkspaceEntry>>();
        public ValueTask<HostTaskObservation?> ReadTaskAsync(HostId<SessionIdentity> session, HostId<TaskIdentity> task, CancellationToken cancellationToken) => Refuse<ValueTask<HostTaskObservation?>>();
        public ValueTask<HostTaskObservation> CancelWaitingTaskAsync(HostRequest control, HostTaskCancellationTarget target, Func<bool> canControl, CancellationToken cancellationToken) => Refuse<ValueTask<HostTaskObservation>>();
        public ValueTask<SessionWorkspaceEntry> CreateNamedSessionAsync(HostRequest request, SessionName name, Func<bool> canControl, CancellationToken cancellationToken) => Refuse<ValueTask<SessionWorkspaceEntry>>();
        public ValueTask<SessionWorkspaceEntry> RenameSessionAsync(HostRequest request, HostRevision expectedGeneration, long expectedMetadataRevision, SessionName name, Func<bool> canControl, CancellationToken cancellationToken) => Refuse<ValueTask<SessionWorkspaceEntry>>();
        public ValueTask<SessionPage<HostQuestionRecord>> ReadQuestionPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) => Refuse<ValueTask<SessionPage<HostQuestionRecord>>>();
        public ValueTask<SessionPage<HostTaskRecord>> ReadTaskPageAsync(HostId<SessionIdentity> session, Guid? after, int limit, CancellationToken cancellationToken) => Refuse<ValueTask<SessionPage<HostTaskRecord>>>();
        public ValueTask<WorkSessionAuthorization> ChangeIdleLifecycleAsync(HostRequest request, HostRevision expectedGeneration, bool active, Func<bool> canControl, CancellationToken cancellationToken) => Refuse<ValueTask<WorkSessionAuthorization>>();
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request, HostRevision generation, Func<bool> admitted, CancellationToken token) => Refuse<ValueTask<HostTaskRecord>>();
        public ValueTask<Kora.Core.Conversation.ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session, CancellationToken token) => Refuse<ValueTask<Kora.Core.Conversation.ConversationDraftSnapshot>>();
        public ValueTask<Kora.Core.Conversation.ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control, Kora.Core.Conversation.ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => Refuse<ValueTask<Kora.Core.Conversation.ConversationDraftSnapshot>>();
        public ValueTask<Kora.Core.Conversation.ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request, HostRevision generation, long expectedMetadataRevision, string body, Func<bool> admitted, CancellationToken token) => Refuse<ValueTask<Kora.Core.Conversation.ConversationSubmissionReceipt>>();
        public ValueTask<Kora.Core.Conversation.ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request, Kora.Core.Dependencies.ModelTurnOutcome outcome, Kora.Core.Dependencies.ModelTurnReason reason, Func<bool> admitted, CancellationToken token) => Refuse<ValueTask<Kora.Core.Conversation.ConversationSubmissionReceipt>>();
        public ValueTask<Kora.Core.Conversation.ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session, Kora.Core.Conversation.ConversationCursor? cursor, int limit, CancellationToken token) => Refuse<ValueTask<Kora.Core.Conversation.ConversationPage>>();
        public ValueTask<Kora.Core.Conversation.ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session, HostId<RequestIdentity> request, CancellationToken token) => Refuse<ValueTask<Kora.Core.Conversation.ConversationMessage>>();
    }
}
