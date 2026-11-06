using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Presentation;
using Kora.Application.Voice;
using Kora.Core.Presentation;
using Kora.Core.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    private const string AutomaticDetailsBody = "Brief response:\nA bounded summary.\n\nDetailed response:\n# Exact answer\n\n```text\nThe complete original result.\n```";
    [Fact]
    public async Task NewFinalizedForegroundAnswerOpensExactPrimaryPaneWithoutChangingKeyboardFocus()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(storage);
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."));
            await using var admission = new AudioControlAdmission(storage.Store, storage.Store, new(storage.Tasks));
            var preferences = new DetailPresentationSettingsTests.Preferences();
            var settings = new DetailPresentationConfigurationService(preferences, admission, new AuditLog(), storage.Time);
            await native.InitializeAsync(null, detailPresentationConfiguration: settings);
            using var settingsWindow = new SettingsWindowController(native.Main, NullLogger<SettingsWindowController>.Instance);
            settingsWindow.Navigate(SettingsSection.Responses, static () => true);
            native.Main.CommandText = "always open detailed results";
            await native.Main.RunTypedCommand.ExecuteAsync();
            preferences.Writes.Should().Be(0);
            native.Main.DetailPresentationReviewText.Should().Contain("OpenAutomatically");
            await native.Main.ConfirmDetailPresentationCommand.ExecuteAsync();
            preferences.Value.Should().Be((DetailPresentationMode)2);

            var release = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var adapter = new MemoryAnswerAdapter { Pending = release.Task };
            await using var composition = new Composition(storage, adapter: adapter);
            var model = composition.Model;
            model.BindDetailPresentation(settings);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == storage.Request.SessionId));
            using var details = new DetailWindowController(native.Documents, native.Main,
                NullLogger<DetailWindowController>.Instance, NullLogger<NativeDetailRenderer>.Instance,
                NullLogger<StaticHtmlDetailRenderer>.Instance);
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance, details);
            var owner = new SessionsPresentationOwner(model, native.Main);
            var window = responses.PrepareSessions(model, owner, static () => { }, static () => { });
            var automatic = new AutomaticDetailsProbe(responses);
            model.BindAutomaticDetails(automatic);
            window.ShowSessions(expanded: true);
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            var composer = window.SessionView.FindControl<TextBox>("SessionComposer")!;
            composer.Focus();
            model.ComposerBody = "Produce a new detailed foreground answer";
            window.IsActive.Should().BeTrue();
            window.SessionView.IsConversationPresented.Should().BeTrue();
            model.CanAutomaticallyPresentDetails.Should().BeTrue();
            native.Main.CaptureAutomaticDetailsAdmission()().Should().BeTrue();
            ((IAutomaticDetailPresentation)responses).CaptureAutomaticDetails().Should().NotBeNull();
            var pending = model.SubmitConversationAsync();
            await adapter.Entered.Task.WaitAsync(storage.Token);
            await WaitForDetailReadinessAsync(model, storage.Token);
            var tasksBeforeFinalization = storage.Count("host_tasks");
            window.UpdateLayout();
            composer.Focus().Should().BeTrue();
            var focus = window.FocusManager!.GetFocusedElement();
            var activations = 0;
            window.Activated += (_, _) => activations++;
            release.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(AutomaticDetailsBody, null)));
            await pending;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            automatic.Leases.Should().ContainSingle().Which.Should().NotBeNull();
            automatic.IntentCurrent.Should().Equal(true);
            automatic.Published.Should().Equal(true);
            window.SessionView.DetailsVisible.Should().BeTrue();
            var reader = window.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single();
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(focus);
            activations.Should().Be(0);
            reader.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
            reader.FindControl<TextBox>("ContentReader")!.Text.Should().Be(AutomaticDetailsBody);
            model.ComposerBody.Should().BeEmpty();
            adapter.Calls.Should().Be(1);
            storage.Count("host_questions").Should().Be(0);
            storage.Count("scoped_grants").Should().Be(0);
            storage.Count("conversation_drafts").Should().Be(0);
            storage.Count("host_tasks").Should().Be(tasksBeforeFinalization);
            native.Main.IsVoiceEnabled.Should().BeFalse();
            window.SessionView.HideDetailsAndRestoreFocus();
            var message = model.ConversationMessages.Single();
            model.PresentOriginalRequestSubmission(new(message, new(message.Request, new(3), message.ExpectedTaskState)));
            window.SessionView.DetailsVisible.Should().BeFalse("dismissal and duplicate finalization cannot reopen the pane");
            automatic.Published.Should().ContainSingle();
            owner.Revoke();
        });
    }

    [Theory]
    [InlineData("Offer")]
    [InlineData("LinkOnly")]
    [InlineData("late-opt-in")]
    [InlineData("preference")]
    [InlineData("expired")]
    [InlineData("composer")]
    [InlineData("native-input")]
    [InlineData("retained-reader")]
    [InlineData("background")]
    [InlineData("foreground-return")]
    [InlineData("rebind")]
    [InlineData("privacy")]
    [InlineData("unqualified")]
    [InlineData("queued")]
    [InlineData("manual-dismissal")]
    [InlineData("revoke-during-show")]
    [InlineData("failed")]
    [InlineData("selection")]
    public async Task ChangedOrUnknownForegroundAdmissionLeavesOnlyExplicitManualAccess(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(storage);
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."));
            await using var admission = new AudioControlAdmission(storage.Store, storage.Store, new(storage.Tasks));
            var preferences = new DetailPresentationSettingsTests.Preferences
            {
                Value = boundary is "Offer" or "late-opt-in" ? DetailPresentationMode.Offer
                    : boundary is "LinkOnly" ? DetailPresentationMode.LinkOnly : DetailPresentationMode.OpenAutomatically
            };
            var clock = new AutomaticDetailsClock();
            var settings = new DetailPresentationConfigurationService(preferences, admission, new AuditLog(), clock);
            await native.InitializeAsync(null, detailPresentationConfiguration: settings);
            var release = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var adapter = new MemoryAnswerAdapter { Pending = release.Task };
            DetailWindowController? detailHost = null;
            ResponseWindow? window = null;
            await using var composition = new Composition(storage, (content, live, resolve, token) =>
                detailHost!.OpenConversationDetailAsync(content, window, live, resolve, token), adapter: adapter);
            var model = composition.Model;
            model.BindDetailPresentation(settings);
            if (boundary is "selection")
            { await composition.Workspace.CreateAsync(new("Other exact conversation"), RequestOrigin.LocalUi, storage.Token); }
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == storage.Request.SessionId));
            using var details = new DetailWindowController(native.Documents, native.Main,
                NullLogger<DetailWindowController>.Instance, NullLogger<NativeDetailRenderer>.Instance,
                NullLogger<StaticHtmlDetailRenderer>.Instance);
            detailHost = details;
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance, details);
            var owner = new SessionsPresentationOwner(model, native.Main);
            window = responses.PrepareSessions(model, owner, static () => { }, static () => { });
            window.ShowSessions(expanded: true);
            window.Activate();
            Dispatcher.UIThread.RunJobs();
            var queued = new TaskCompletionSource<ConversationSubmissionReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            composition.Conversation.SubmittedConversationCompleted += (_, completion) =>
            {
                if (completion.Receipt is { } receipt && string.Equals(receipt.Message.Body, "Queued original request", StringComparison.Ordinal))
                { queued.TrySetResult(receipt); }
            };
            model.ComposerBody = "New original request";
            var pending = model.SubmitConversationAsync();
            var body = boundary is "unqualified" ? DetailAnswerAdapter.Body : AutomaticDetailsBody;
            try
            {
                await adapter.Entered.Task.WaitAsync(storage.Token);
                await WaitForDetailReadinessAsync(model, storage.Token);
                window.SessionView.DetailsVisible.Should().BeFalse("provisional results never open a reader");
                if (boundary is "late-opt-in")
                { preferences.Value = DetailPresentationMode.OpenAutomatically; settings.Observe(); }
                if (boundary is "preference") { settings.HoldUnavailable(); }
                if (boundary is "expired") { clock.Now = clock.Now.AddMinutes(2); }
                if (boundary is "composer") { model.ComposerBody = "Independent unsent input"; }
                if (boundary is "native-input")
                { native.Main.CommandText = "independent original input"; }
                if (boundary is "retained-reader") { await model.ReadConversationAsync(); }
                if (boundary is "background" or "foreground-return")
                {
                    window.Hide();
                    if (boundary is "foreground-return")
                    { window.ShowSessions(expanded: true); window.Activate(); Dispatcher.UIThread.RunJobs(); }
                }
                if (boundary is "rebind") { model.BindDetailPresentation(settings); }
                if (boundary is "manual-dismissal") { window.SessionView.HideDetailsAndRestoreFocus(); }
                if (boundary is "queued")
                {
                    model.ComposerBody = "Queued original request";
                    await model.SubmitConversationAsync();
                }
                if (boundary is "selection")
                {
                    var original = model.SelectedSessionRecord;
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != storage.Request.SessionId));
                    await model.SelectAsync(original);
                }
                if (boundary is "revoke-during-show")
                {
                    var revoked = false;
                    window.SessionView.FindControl<Control>("DetailsPane")!.PropertyChanged += (_, args) =>
                    {
                        if (!revoked && args.Property == Avalonia.Visual.IsVisibleProperty
                            && window.SessionView.DetailsVisible)
                        { revoked = true; details.ClearForPrivacy(); }
                    };
                }
                if (boundary is "privacy")
                {
                    native.Main.CloseForObservedPrivacyEvent("Deterministic automatic Details revocation", hidePresentation: true);
                    await native.Main.PrivacyClosureTask;
                }
            }
            finally
            {
                release.TrySetResult(boundary is "failed"
                    ? new(ModelTurnOutcome.Unavailable, ModelProviderSelection.OllamaCandidate)
                    : new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(body, null)));
                await pending;
            }
            if (boundary is "queued") { await queued.Task.WaitAsync(storage.Token); }
            Dispatcher.UIThread.RunJobs();
            window.SessionView.DetailsVisible.Should().BeFalse(boundary);
            if (boundary is "composer") { model.ComposerBody.Should().Be("Independent unsent input"); }
            if (boundary is not "privacy")
            {
                window.ShowSessions(expanded: true);
                window.Activate();
                Dispatcher.UIThread.RunJobs();
                window.SessionView.DetailsVisible.Should().BeFalse("foreground callbacks cannot replay terminal results");
                if (!model.ObservedConversationMessages.Any()) { await model.ReadConversationAsync(); }
                var message = model.ObservedConversationMessages.Last(record => record.Outcome is not null);
                await model.OpenConversationDetailAsync(message, assistant: message.AssistantResponse is not null);
                window.SessionView.DetailsVisible.Should().BeTrue("the preference and failed automatic admission do not revoke manual access");
                window.UpdateLayout();
                var reader = window.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single();
                reader.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
                reader.FindControl<TextBox>("ContentReader")!.Text.Should().Be(message.AssistantResponse is not null ? body : message.Body);
            }
            adapter.Calls.Should().Be(boundary is "queued" ? 2 : 1);
            storage.Count("host_questions").Should().Be(0);
            storage.Count("scoped_grants").Should().Be(0);
            storage.Count("conversation_drafts").Should().Be(0);
            owner.Revoke();
        });
    }

    private sealed class AutomaticDetailsClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AutomaticDetailsProbe(IAutomaticDetailPresentation actual) : IAutomaticDetailPresentation
    {
        internal List<AutomaticDetailLease?> Leases { get; } = [];
        internal List<bool> IntentCurrent { get; } = [];
        internal List<bool> Published { get; } = [];
        public AutomaticDetailLease? CaptureAutomaticDetails()
        {
            var lease = actual.CaptureAutomaticDetails();
            Leases.Add(lease);
            return lease;
        }
        public bool PublishAutomaticDetails(AutomaticDetailIntent intent)
        {
            IntentCurrent.Add(intent.IsCurrent());
            var published = actual.PublishAutomaticDetails(intent);
            Published.Add(published);
            return published;
        }
    }
}
