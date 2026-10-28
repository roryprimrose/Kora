using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NamedOpeningActualTypedAndActivatedVoiceChoicesRevealOnlyAcknowledgedRealHistory(bool voice, bool expanded)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new NamedOpeningFixture(voice);
            await f.Initialize(expanded);
            var originalWindow = f.Window;
            var prior = f.Model.SelectedSessionRecord!;
            var draft = await f.Store.Store.ReadConversationDraftAsync(prior.Authority.SessionId, f.Store.Token);
            var audits = f.Store.Count("security_audit_events");
            var tasks = f.Store.Count("host_tasks");
            var sources = await f.Store.Store.ReadMetadataPageAsync(null, 25, f.Store.Token);
            var expected = sources.Records.Where(entry =>
                string.Equals(entry.Metadata?.Name.Value, "Shared topic", StringComparison.Ordinal)).ElementAt(1);
            var observedOrigin = new List<RequestOrigin>();
            f.Model.PropertyChanged += (_, args) =>
            {
                if (string.Equals(args.PropertyName, nameof(SessionsViewModel.ConversationMessages), StringComparison.Ordinal)
                    && f.Model.SelectedSessionRecord == expected && f.Model.ConversationMessages.Count != 0)
                { observedOrigin.Add(HostActivity.RequireCurrent().Request.Origin); }
            };
            var opening = f.ObserveOpening();
            var command = f.Start("open conversation \"Shared topic\"", voice);
            var choice = await opening.WaitAsync(TimeSpan.FromSeconds(20), f.Store.Token);
            var choices = NamedControl<ComboBox>(choice, "NamedConversationChoices");
            var confirm = NamedControl<Button>(choice, "OpenNamedConversation");
            choices.ItemCount.Should().Be(2);
            choices.SelectedIndex.Should().Be(-1, "there is no arbitrary first match");
            confirm.IsEnabled.Should().BeFalse();
            f.Model.SelectedSessionRecord.Should().Be(prior);
            f.Model.ComposerBody.Should().Be("other conversation memory only");
            f.Model.ConversationMessages.Should().BeEmpty();
            choices.SelectedIndex = 1;
            choices.SelectedItem!.ToString().Should().Contain("Shared topic").And.NotContain(expected.Authority.SessionId.Value.ToString("D"));
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await command;
            await f.Controller.NamedConversationOperation;
            Dispatcher.UIThread.RunJobs();
            f.Window.Should().BeSameAs(originalWindow);
            f.Model.SelectedSessionRecord.Should().Be(expected);
            var history = f.Model.ConversationMessages.Should().ContainSingle().Subject;
            history.Request.SessionId.Should().Be(expected.Authority.SessionId);
            history.AssistantResponse!.Body.Should().Be(expected.Authority.SessionId == f.First.Authority.SessionId
                ? "earlier ASSISTANT" : "genuine retained follow-up");
            f.Window!.SessionView.GetVisualDescendants().OfType<TextBlock>()
                .Should().Contain(text => text.Text == history.AssistantResponse.Body, "the native conversation must render the real answer");
            f.Window.SessionView.FindControl<TabControl>("ConversationTabs")!.IsKeyboardFocusWithin.Should().BeTrue();
            f.Adapter.Calls.Should().Be(2, "opening is never another model request");
            observedOrigin.Should().NotBeEmpty().And.OnlyContain(origin =>
                origin == (voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi));
            f.Store.Count("conversation_messages").Should().Be(2);
            f.Store.Count("host_tasks").Should().Be(tasks);
            f.Store.Count("security_audit_events").Should().Be(audits);
            (await f.Store.Store.ReadConversationDraftAsync(prior.Authority.SessionId, f.Store.Token)).Should().Be(draft);
            choice.Content.Should().BeNull();
            choice.IsVisible.Should().BeFalse();
            await f.Model.PresentNamedConversationAsync(prior, static () => true,
                voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi, f.Store.Token);
            f.Model.ComposerBody.Should().Be("other conversation memory only");
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("rename")]
    [InlineData("lifecycle")]
    [InlineData("delete")]
    [InlineData("control")]
    [InlineData("private")]
    [InlineData("expiry")]
    [InlineData("input")]
    [InlineData("selection")]
    [InlineData("dispose")]
    [InlineData("hidden")]
    public async Task NamedOpeningWithdrawalsCannotRevealAnotherHistoryOrReviveLateAcknowledgment(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new NamedOpeningFixture(false);
            await f.Initialize(expanded: true);
            var prior = f.Model.SelectedSessionRecord!;
            var opening = f.ObserveOpening();
            var command = f.Start("read conversation \"Shared topic\"", false);
            var choice = await opening.WaitAsync(TimeSpan.FromSeconds(20), f.Store.Token);
            var choices = NamedControl<ComboBox>(choice, "NamedConversationChoices");
            choices.SelectedIndex = 0;
            var late = NamedControl<Button>(choice, "OpenNamedConversation");
            switch (change)
            {
                case "cancel": NamedControl<Button>(choice, "CancelNamedConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
                case "rename":
                    await f.Composed.Workspace.RenameAsync(f.First.Authority.SessionId, f.First.Authority.Generation,
                        f.First.Metadata!.Revision.Value, new("Changed"), RequestOrigin.LocalUi, f.Store.Token);
                    break;
                case "lifecycle":
                    await f.Composed.Workspace.ChangeLifecycleAsync(f.First.Authority.SessionId, f.First.Authority.Generation,
                        false, RequestOrigin.LocalUi, f.Store.Token);
                    break;
                case "delete":
                    using (HostActivity.BeginRoot(new(new(Guid.NewGuid()), f.First.Authority.SessionId,
                        new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request))
                    {
                    var review = await f.Composed.Workspace.PreviewDispositionAsync(f.First.Authority.SessionId,
                        f.First.Authority.Generation, f.First.Metadata!.Revision.Value, f.Store.Token);
                    await f.Composed.Workspace.ConfirmDispositionAsync(review, RequestOrigin.LocalUi, static () => true, f.Store.Token);
                    }
                    break;
                case "control": f.Composed.Access.ControlRevision++; break;
                case "private": f.Composed.Access.CanInspect = false; break;
                case "expiry": f.Clock.Now = f.Clock.Now.AddMinutes(2); break;
                case "input": f.Native.Main.CommandText = "replaced original input"; break;
                case "selection":
                    await f.Model.SelectAsync(f.Model.Sessions.Single(row => row.Authority.SessionId == f.First.Authority.SessionId));
                    break;
                case "dispose": f.Controller.Dispose(); break;
                case "hidden": choice.Hide(); break;
            }
            late.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await command;
            await f.Controller.NamedConversationOperation;
            late.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            f.Adapter.Calls.Should().Be(2);
            f.Store.Count("conversation_messages").Should().Be(change is "delete" ? 1 : 2);
            f.Model.ConversationMessages.Should().BeEmpty();
            if (change is not ("private" or "control" or "selection" or "dispose"))
            {
                f.Model.SelectedSessionRecord.Should().Be(prior);
                f.Model.ComposerBody.Should().Be("other conversation memory only");
            }
            choice.IsVisible.Should().BeFalse();
            choice.Content.Should().BeNull();
        });
    }

    [Fact]
    public async Task NamedOpeningMissingNameDoesNotCreateFallbackOrChangeOriginalInput()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new NamedOpeningFixture(false);
            await f.Initialize(expanded: true);
            var prior = f.Model.SelectedSessionRecord;
            await f.Start("open conversation \"Missing\"", false);
            f.Native.Main.ResponseBody.Should().Contain("No exact readable-name match");
            f.Model.SelectedSessionRecord.Should().Be(prior);
            f.Model.ComposerBody.Should().Be("other conversation memory only");
            f.Adapter.Calls.Should().Be(2);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedOpeningLateHistoryReadJoinsWithoutPublishingAfterSourceOrControlRetirement(bool control)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new NamedOpeningFixture(false);
            await f.Initialize(expanded: true);
            var prior = f.Model.SelectedSessionRecord;
            var opening = f.ObserveOpening();
            var command = f.Start("open conversation \"Shared topic\"", false);
            var choice = await opening.WaitAsync(TimeSpan.FromSeconds(20), f.Store.Token);
            NamedControl<ComboBox>(choice, "NamedConversationChoices").SelectedIndex = 0;
            f.HoldHistory = true;
            NamedControl<Button>(choice, "OpenNamedConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await f.HistoryEntered.Task.WaitAsync(f.Store.Token);
            f.Controller.NamedConversationOperation.IsCompleted.Should().BeFalse();
            if (control) { f.Composed.Access.ControlRevision++; }
            else
            {
                await f.Composed.Workspace.RenameAsync(f.First.Authority.SessionId, f.First.Authority.Generation,
                    f.First.Metadata!.Revision.Value, new("Changed while reading"), RequestOrigin.LocalUi, f.Store.Token);
            }
            f.Controller.NamedConversationOperation.IsCompleted.Should().BeFalse("revocation cannot manufacture a physical read join");
            f.HistoryRelease.SetResult();
            var joinedCommand = async () => await command;
            if (control) { await joinedCommand.Should().ThrowAsync<InvalidOperationException>(); }
            else { await joinedCommand.Should().ThrowAsync<OperationCanceledException>(); }
            var joined = async () => await f.Controller.NamedConversationOperation;
            if (control) { await joined.Should().ThrowAsync<InvalidOperationException>(); }
            else { await joined.Should().ThrowAsync<OperationCanceledException>(); }
            f.HistoryJoined.Task.IsCompletedSuccessfully.Should().BeTrue();
            f.Model.ConversationMessages.Should().BeEmpty();
            if (!control)
            {
                f.Model.SelectedSessionRecord.Should().Be(prior);
                f.Model.ComposerBody.Should().Be("other conversation memory only");
            }
            f.Adapter.Calls.Should().Be(2);
            f.Store.Count("conversation_messages").Should().Be(2);
        });
    }

    [Fact]
    public async Task NamedOpeningKeepsAnUnchangedLaterHistoryPageAndCurrentUnsentInput()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new NamedOpeningFixture(false);
            await f.Initialize(expanded: true);
            for (var index = 0; index < 26; index++)
            {
                var request = new HostRequest(new(Guid.NewGuid()), f.First.Authority.SessionId,
                    new(Guid.NewGuid()), RequestOrigin.LocalUi);
                using var root = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
                await f.Store.Store.RetainConversationMessageAsync(request, f.First.Authority.Generation,
                    f.First.Metadata!.Revision.Value, "history " + index, static () => true, f.Store.Token);
                await f.Store.Store.CompleteConversationSubmissionAsync(request,
                    Kora.Core.Dependencies.ModelTurnOutcome.Unavailable, Kora.Core.Dependencies.ModelTurnReason.UnregisteredModel,
                    static () => true, f.Store.Token);
            }
            await f.Model.SelectAsync(f.Model.Sessions.Single(row => row.Authority.SessionId == f.First.Authority.SessionId));
            await f.Model.ReadConversationAsync();
            await f.Model.ReadConversationAsync(next: true);
            var laterPage = f.Model.ConversationMessages.ToArray();
            laterPage.Select(message => message.Body).Should().Equal("history 24", "history 25");
            f.Model.ComposerBody = "current exact session memory";
            var metadata = await f.Store.Store.ReadMetadataPageAsync(null, 25, f.Store.Token);
            var matches = metadata.Records.Where(entry =>
                string.Equals(entry.Metadata?.Name.Value, "Shared topic", StringComparison.Ordinal)).ToArray();
            var opening = f.ObserveOpening();
            var command = f.Start("open conversation \"Shared topic\"", false);
            var choice = await opening.WaitAsync(TimeSpan.FromSeconds(20), f.Store.Token);
            NamedControl<ComboBox>(choice, "NamedConversationChoices").SelectedIndex = Array.FindIndex(matches,
                entry => entry.Authority.SessionId == f.First.Authority.SessionId);
            NamedControl<Button>(choice, "OpenNamedConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await command;
            f.Model.ConversationMessages.Should().Equal(laterPage);
            f.Model.ConversationMessages[0].Should().BeSameAs(laterPage[0], "the existing reading snapshot is not replaced with the first page");
            f.Model.ComposerBody.Should().Be("current exact session memory");
            f.Adapter.Calls.Should().Be(2);
            (await f.Store.Store.ReadConversationDraftAsync(f.First.Authority.SessionId, f.Store.Token)).Draft.Body.Should().BeNull();
        });
    }

    private sealed class NamedOpeningFixture : IAsyncDisposable
    {
        internal InteractionStorageFixture Store { get; } = new();
        internal HistoryAdapter Adapter { get; } = new();
        internal Composition Composed { get; private set; } = null!;
        internal NativeUxFixtureSession Native { get; }
        internal SessionsWindowController Controller { get; private set; } = null!;
        internal ResponseWindow? Window { get; private set; }
        internal SessionsViewModel Model => (SessionsViewModel)Window!.SessionView.DataContext!;
        internal SessionWorkspaceEntry First { get; private set; } = null!;
        internal NamedOpeningClock Clock { get; } = new();
        internal bool HoldHistory { get; set; }
        internal TaskCompletionSource HistoryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource HistoryRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource HistoryJoined { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ResponseWindowController? responses;
        private SessionConversationService? passiveReader;
        private AllWorkWindowController? all;
        private WorkspaceSurfaceCommandController? commands;
        private IDisposable? openingObservation;

        internal NamedOpeningFixture(bool voice) => Native = new(Path.GetFullPath("."), syntheticVoice: voice);

        internal async Task Initialize(bool expanded)
        {
            await Store.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(Store);
            Composed = new(Store, adapter: Adapter);
            var original = await Store.Store.ReadMetadataAsync(Store.Request.SessionId, Store.Token);
            First = await Composed.Workspace.RenameAsync(original.Authority.SessionId, original.Authority.Generation,
                original.Metadata?.Revision.Value ?? 0, new("Shared topic"), RequestOrigin.LocalUi, Store.Token);
            var second = await Composed.Workspace.CreateAsync(new("Shared topic"), RequestOrigin.LocalUi, Store.Token);
            var other = await Composed.Workspace.CreateAsync(new("Other topic"), RequestOrigin.LocalUi, Store.Token);
            await Composed.Model.RefreshAsync();
            foreach (var target in new[] { First, second })
            {
                await Composed.Model.SelectAsync(Composed.Model.Sessions.Single(row => row.Authority.SessionId == target.Authority.SessionId));
                Composed.Model.ComposerBody = target == First ? "first real original" : "second real original";
                await Composed.Model.SubmitConversationAsync();
            }
            Adapter.Calls.Should().Be(2);
            await Native.InitializeAsync();
            if (Native.Voice.SyntheticCapture)
            {
                Native.Main.BindVoiceOwnershipGate(static () => true);
                await Native.Main.SetVoiceConsentAsync(true);
                await Native.Main.BeginPushToTalkAsync();
                await Native.Main.EndPushToTalkAsync();
            }
            responses = new(Native.Main, NullLogger<ResponseWindowController>.Instance);
            var reader = new SearchReader(ReadHistory, readMetadata: Store.Store.ReadMetadataAsync);
            var readWorkspace = new SessionWorkspaceService(reader, new(Store.Tasks), Composed.Access,
                NullLogger<SessionWorkspaceService>.Instance);
            passiveReader = new(Store.Store, Store.Store, readWorkspace, Composed.Access,
                Composed.Turns, Store.Time, NullLogger<SessionConversationService>.Instance);
            Controller = new(Native.Main, Composed.Workspace, Native.Evidence, Composed.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: passiveReader)
            { NamedConversationTime = Clock };
            Controller.CreateInteraction = (model, owner) =>
                Window = responses.PrepareSessions(model, owner, Controller.Open, static () => { });
            Controller.Bind();
            all = new(Native.Main, Composed.Workspace, Composed.Access, NullLogger<AllWorkViewModel>.Instance, Controller);
            commands = new(Native.Main, Composed.Access, all, Controller);
            commands.Bind();
            await Controller.ShowInteractionAsync(true, static () => true);
            await Model.SelectAsync(Model.Sessions.Single(row => row.Authority.SessionId == other.Authority.SessionId));
            await Model.ReadConversationAsync();
            Model.ComposerBody = "other conversation memory only";
            Window!.ShowSessions(expanded);
        }

        private async ValueTask<ConversationPage> ReadHistory(HostId<SessionIdentity> session,
            ConversationCursor? cursor, int limit, CancellationToken token)
        {
            var page = await Store.Store.ReadConversationPageAsync(session, cursor, limit, token);
            if (HoldHistory)
            {
                HistoryEntered.TrySetResult();
                await HistoryRelease.Task;
                HistoryJoined.TrySetResult();
            }
            return page;
        }

        internal Task<NamedConversationChoiceWindow> ObserveOpening()
        {
            var opened = new TaskCompletionSource<NamedConversationChoiceWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            openingObservation = Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<NamedConversationChoiceWindow>((window, _) => opened.TrySetResult(window));
            return opened.Task;
        }

        internal Task Start(string text, bool voice)
        {
            if (voice)
            {
                Native.Voice.PublishTranscript(Native.Main.AssistantName + ", " + text);
                return Task.CompletedTask;
            }
            Native.Main.CommandText = text;
            return Native.Main.RunTypedCommand.ExecuteAsync();
        }

        public async ValueTask DisposeAsync()
        {
            openingObservation?.Dispose();
            commands?.Dispose();
            all?.Dispose();
            Controller?.Dispose();
            if (Controller is not null && !Controller.NamedConversationOperation.IsCompleted)
            { await Controller.NamedConversationOperation; }
            responses?.Dispose();
            await Native.Main.ExitAsync();
            Native.Dispose();
            if (passiveReader is not null) { await passiveReader.DisposeAsync(); }
            if (Composed is not null) { await Composed.DisposeAsync(); }
            Store.Dispose();
        }
    }

    private static T NamedControl<T>(NamedConversationChoiceWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => string.Equals(control.Name, name, StringComparison.Ordinal));

    private sealed class NamedOpeningClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
