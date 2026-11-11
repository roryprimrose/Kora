using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    private const string CollectionAnswer = "set appearance.theme to dark\nset work.active-deadline-minutes to 10\nset speech.playback-volume to 0";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingCollectionNativeProductionControllerIndependentlyConfirmsTwoMembersAndReopensActualPreferences(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new SettingCollectionFixture();
            await f.Initialize(compact);
            var model = f.Model;
            var source = model.ConversationMessages.Single();
            await f.Click("ReviewRegisteredSettingSuggestion");
            model.HasRegisteredSettingCollection.Should().BeTrue(model.ConversationStatus);
            model.RegisteredSettingCollectionText.Should().Be(CollectionAnswer);
            f.Surface.FindControl<TextBox>("RegisteredSettingCollectionReadback")!.Text.Should().Be(CollectionAnswer);
            var choice = f.Surface.FindControl<ComboBox>("RegisteredSettingCollectionChoice")!;
            choice.SelectedItem.Should().BeNull();
            model.SelectedRegisteredSettingMember.Should().BeNull();
            model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
            model.CanReviewSelectedRegisteredSetting.Should().BeFalse();
            f.DeadlinePreferences.Load().Should().BeNull();
            f.AppearancePreferences.LoadThemeMode().Should().BeNull();
            f.Audit.Events.Should().BeEmpty();

            choice.SelectedItem = model.RegisteredSettingCollectionMembers.Single(item => item.Setting == RegisteredSetting.OrdinaryTurnDeadline);
            model.SelectedRegisteredSettingMember!.Setting.Should().Be(RegisteredSetting.OrdinaryTurnDeadline);
            await f.Click("ReviewSelectedRegisteredSetting");
            model.HasRegisteredSettingCollection.Should().BeFalse("selection is consumed; another member requires a new deliberate inspection and selection");
            model.RegisteredSettingSuggestionDescription.Should().Contain("Current saved: default five minutes")
                .And.Contain("Proposed: 10 minutes").And.Contain("Default: five minutes").And.Contain("shared device-local");
            f.DeadlinePreferences.Load().Should().BeNull("member review is not confirmation");
            await f.Click("ConfirmRegisteredSettingSuggestion");
            new LocalOrdinaryTurnDeadlinePreferences(f.Storage.Paths).Load().Should().Be(new OrdinaryTurnDeadline(10));
            f.Deadline.Capture().Should().Be(new OrdinaryTurnDeadline(10));
            model.ConversationStatus.Should().Contain("saved with committed preference and audit readback");
            f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            f.AppearancePreferences.LoadThemeMode().Should().BeNull("the first confirmation cannot apply another member");
            model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
            model.SelectedRegisteredSettingMember.Should().BeNull();

            await f.Click("ReviewRegisteredSettingSuggestion");
            choice.SelectedItem.Should().BeNull("reinspection must not auto-select the previous or next member");
            choice.SelectedItem = model.RegisteredSettingCollectionMembers.Single(item => item.Setting == RegisteredSetting.Theme);
            await f.Click("ReviewSelectedRegisteredSetting");
            model.RegisteredSettingSuggestionDescription.Should().Contain("current system (default); proposed dark mode")
                .And.Contain("shared device-local");
            f.AppearancePreferences.LoadThemeMode().Should().BeNull();
            await f.Click("ConfirmRegisteredSettingSuggestion");
            f.AppearancePreferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
            f.Native.Main.ThemeMode.Should().Be(ApplicationThemeMode.Dark);
            var reopened = new AppearanceConfigurationService(
                new LocalAppearancePreferences(f.Native, NullLogger<LocalAppearancePreferences>.Instance),
                new AuditLog(), NullLogger<AppearanceConfigurationService>.Instance);
            reopened.Get(AppearanceOption.Theme).Value.Should().Be(new AppearanceValue.Theme(ApplicationThemeMode.Dark));
            reopened.Get(AppearanceOption.Theme).IsSaved.Should().BeTrue();
            using var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Evidence);
            var themeAudit = await f.Native.Evidence.QueryAsync(new()
            {
                Source = EvidenceSource.Audit,
                ActionId = AppearanceOptionRegistry.Get(AppearanceOption.Theme).AuditAction,
            }, null, f.Storage.Token);
            themeAudit.Status.Should().Be(EvidencePageStatus.Available);
            themeAudit.Cursor.Should().BeNull();
            themeAudit.Records.Select(item => item.Audit!.Outcome).Should()
                .BeEquivalentTo(new[] { SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded });
            themeAudit.Records.Should().OnlyContain(item => item.Host != null
                && item.Host.SessionId == source.Request.SessionId);
            new LocalPlaybackVolumePreferences(f.Native).Load().Should().BeNull("the unchosen volume member must never write");
            f.Audit.Events.Should().HaveCount(2, "the second member cannot repeat the deadline writer");
            model.HasRegisteredSettingCollection.Should().BeFalse();
            model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
            model.ComposerBody.Should().Be("Unsent text is unchanged.");
            f.Adapter.Calls.Should().Be(1);
            (await f.Storage.Store.ReadConversationMessageAsync(source.Request.SessionId, source.Request.RequestId, f.Storage.Token))
                .Should().Be(source);
            f.Storage.Count("host_questions").Should().Be(0);
            f.Storage.Count("scoped_grants").Should().Be(0);
        });
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("malformed")]
    [InlineData("oversize")]
    [InlineData("commentary")]
    public async Task SettingCollectionNativeRejectsTheWholeAnswerBeforeAnySelectionOrWrite(string invalid)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var text = invalid switch
            {
                "unknown" => CollectionAnswer + "\nreset unknown.setting",
                "duplicate" => CollectionAnswer + "\nreset appearance.theme",
                "malformed" => CollectionAnswer + "\nset speech.summary-words to 00",
                "oversize" => CollectionAnswer + "\n" + new string('x', 3073),
                _ => CollectionAnswer + "\nI saved all of these.",
            };
            await using var f = new SettingCollectionFixture(text);
            await f.Initialize(false);
            await f.Click("ReviewRegisteredSettingSuggestion");
            f.Model.HasRegisteredSettingCollection.Should().BeFalse();
            f.Model.RegisteredSettingCollectionMembers.Should().BeEmpty();
            f.Model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
            f.AppearancePreferences.LoadThemeMode().Should().BeNull();
            f.DeadlinePreferences.Load().Should().BeNull();
            f.Audit.Events.Should().BeEmpty();
            f.Adapter.Calls.Should().Be(1);
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("page")]
    [InlineData("control")]
    [InlineData("privacy")]
    [InlineData("session")]
    [InlineData("review-await")]
    [InlineData("effect-await")]
    [InlineData("generation-await")]
    [InlineData("source-await")]
    [InlineData("policy")]
    [InlineData("uncertain")]
    public async Task SettingCollectionRetiresAcrossNativeSourceChangesAndNeverContinuesAfterAnUncertainEffect(string transition)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new SettingCollectionFixture(failSuccess: transition is "uncertain");
            await f.Initialize(false);
            var model = f.Model;
            var source = model.ConversationMessages.Single();
            await model.ReviewRegisteredSettingSuggestionAsync(source);
            model.HasRegisteredSettingCollection.Should().BeTrue();
            model.SelectedRegisteredSettingMember = model.RegisteredSettingCollectionMembers.Single(item => item.Setting == RegisteredSetting.OrdinaryTurnDeadline);
            if (transition is "review-await" or "effect-await" or "generation-await" or "source-await")
            {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var reads = 0;
                var original = model.ReviewCollectionOrdinaryDeadline!;
                model.ReviewCollectionOrdinaryDeadline = (message, current, validate, selected, token) =>
                    original(message, current, async cancellation =>
                    {
                        reads++;
                        if (transition is "review-await" && reads == 1 || transition is not "review-await" && reads == 3)
                        {
                            entered.TrySetResult();
                            await release.Task.WaitAsync(cancellation);
                        }
                        await validate(cancellation);
                    }, selected, token);
                var work = model.ReviewSelectedRegisteredSettingAsync();
                if (transition is not "review-await") { await work; work = model.ConfirmRegisteredSettingSuggestionAsync(); }
                await Task.WhenAny(entered.Task, work);
                entered.Task.IsCompleted.Should().BeTrue(model.ConversationStatus);
                if (transition is "generation-await")
                {
                    var done = await f.Composed.Workspace.ChangeLifecycleAsync(source.Request.SessionId,
                        source.Generation, false, RequestOrigin.LocalUi, f.Storage.Token);
                    var resumed = await f.Composed.Workspace.ChangeLifecycleAsync(done.SessionId,
                        done.Generation, true, RequestOrigin.LocalUi, f.Storage.Token);
                    resumed.Generation.Value.Should().Be(source.Generation.Value + 2);
                }
                else if (transition is "source-await")
                {
                    var request = new HostRequest(new(Guid.NewGuid()), source.Request.SessionId,
                        new(Guid.NewGuid()), RequestOrigin.LocalUi);
                    using var input = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
                    var preview = await f.Storage.Store.PreviewDispositionAsync(source.Request.SessionId,
                        source.Generation, 0, f.Storage.Token);
                    await f.Storage.Tasks.CommitAsync(new(request, new(1), HostTaskState.IntentRecorded), 0, f.Storage.Token);
                    await f.Storage.Store.DisposeSessionAsync(request, preview, () => true, f.Storage.Token);
                }
                else { model.CancelRegisteredSettingSuggestion(); }
                release.TrySetResult();
                await work;
            }
            else
            {
                await model.ReviewSelectedRegisteredSettingAsync();
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue(model.ConversationStatus);
                if (transition is "cancel") { model.CancelRegisteredSettingSuggestion(); }
                if (transition is "page") { await model.ReadConversationAsync(); }
                if (transition is "control") { f.Composed.Access.ControlRevision++; }
                if (transition is "privacy") { f.Composed.Access.CanInspect = false; }
                if (transition is "session") { await model.SelectAsync(null); }
                if (transition is "policy")
                {
                    f.Call.Set(CallState.Active);
                    f.Native.Main.IsProtectedCall.Should().BeTrue();
                }
                await model.ConfirmRegisteredSettingSuggestionAsync();
            }
            model.HasRegisteredSettingCollection.Should().BeFalse();
            model.RegisteredSettingCollectionText.Should().BeEmpty();
            model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
            model.SelectedRegisteredSettingMember.Should().BeNull();
            f.AppearancePreferences.LoadThemeMode().Should().BeNull();
            f.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
            if (transition is "uncertain")
            {
                f.DeadlinePreferences.ReadBack().Should().Be(new OrdinaryTurnDeadline(10));
                model.ConversationStatus.Should().Contain("save is unconfirmed").And.Contain("No automatic retry or rollback");
                var before = f.Audit.Events.Count;
                await model.ConfirmRegisteredSettingSuggestionAsync();
                f.Audit.Events.Should().HaveCount(before);
            }
            else { f.DeadlinePreferences.Load().Should().BeNull(); }
            f.Adapter.Calls.Should().Be(1);
        });
    }

    private sealed class SettingCollectionFixture(string text = CollectionAnswer, bool failSuccess = false) : IAsyncDisposable
    {
        internal InteractionStorageFixture Storage { get; } = new();
        internal DeadlineAnswerAdapter Adapter { get; } = new(text);
        internal DeadlineSuggestionAudit Audit { get; } = new() { FailSuccess = failSuccess };
        internal SettingCollectionCallState Call { get; } = new();
        internal Composition Composed { get; private set; } = null!;
        internal NativeUxFixtureSession Native { get; private set; } = null!;
        internal Window Surface { get; private set; } = null!;
        internal SessionsViewModel Model => (SessionsViewModel)Surface.DataContext!;
        internal LocalOrdinaryTurnDeadlinePreferences DeadlinePreferences { get; private set; } = null!;
        internal LocalAppearancePreferences AppearancePreferences { get; private set; } = null!;
        internal OrdinaryTurnDeadlineConfigurationService Deadline { get; private set; } = null!;
        private AudioControlAdmission? admission;
        private SessionsWindowController? controller;

        internal async Task Initialize(bool compact)
        {
            await Storage.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(Storage);
            admission = new(Storage.Store, Storage.Store, new HostTaskCoordinator(Storage.Tasks));
            DeadlinePreferences = new(Storage.Paths);
            Deadline = new(DeadlinePreferences, admission, Audit);
            Composed = new(Storage, adapter: Adapter);
            Native = new(Storage.Paths.LocalRoot);
            await Native.InitializeAsync(null, callState: Call, ordinaryDeadlineConfiguration: Deadline);
            AppearancePreferences = new(Native, NullLogger<LocalAppearancePreferences>.Instance);
            controller = new(Native.Main, Composed.Workspace, Native.Evidence, Composed.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: Composed.Conversation);
            Window? opened = null;
            using var expanded = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => opened = view);
            using var small = Window.WindowOpenedEvent.AddClassHandler<CompactSessionWindow>((view, _) => opened = view);
            if (compact) { controller.OpenCompact(); }
            else { controller.Open(); }
            Surface = opened ?? throw new InvalidOperationException("The production collection surface did not open.");
            await Model.RefreshAsync();
            await Model.SelectAsync(Model.Sessions.Single(entry => entry.Authority.SessionId == Storage.Request.SessionId));
            await Model.ReadConversationAsync();
            Model.ComposerBody = "Suggest registered preferences, without changing anything.";
            await Model.SubmitConversationAsync();
            Model.ComposerBody = "Unsent text is unchanged.";
            await ActivateOriginalRequestWindow(Surface);
        }

        internal async Task Click(string name)
        {
            foreach (var expander in Surface.GetVisualDescendants().OfType<Expander>()) { expander.IsExpanded = true; }
            Surface.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var button = Surface.GetVisualDescendants().OfType<Button>().Single(item => string.Equals(item.Name, name, StringComparison.Ordinal));
            button.IsEnabled.Should().BeTrue(Model.ConversationStatus);
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(object? sender, PropertyChangedEventArgs args)
            {
                if (string.Equals(args.PropertyName, nameof(SessionsViewModel.CanReadConversation), StringComparison.Ordinal)
                    && Model.CanReadConversation) { finished.TrySetResult(); }
            }
            Model.PropertyChanged += Changed;
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await finished.Task.WaitAsync(Storage.Token);
            }
            finally { Model.PropertyChanged -= Changed; }
        }

        public async ValueTask DisposeAsync()
        {
            controller?.Dispose();
            if (Native is not null) { await Native.DisposeAsync(); }
            if (Composed is not null) { await Composed.DisposeAsync(); }
            if (admission is not null) { await admission.DisposeAsync(); }
            Storage.Dispose();
        }

        internal sealed class SettingCollectionCallState : ICallStateService
        {
            public CallState CurrentState { get; private set; } = CallState.Clear;
            public event EventHandler<CallStateChangedEventArgs>? StateChanged;
            internal void Set(CallState state)
            {
                CurrentState = state;
                StateChanged?.Invoke(this, new(state));
            }
        }
    }
}
