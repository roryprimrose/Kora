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
    [Theory]
    [InlineData("live")]
    [InlineData("source")]
    [InlineData("native")]
    [InlineData("privacy")]
    public Task RetainedPreferenceFailurePreservesOldReadersOnlyWhileExactOriginalAdmissionRemainsLive(string boundary) =>
        HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(storage);
            var first = await RetainSearchAnswer(storage, "# Original answer\n\nmarker first");
            var second = await RetainSearchAnswer(storage, "# New answer\n\nmarker second");
            await using var admission = new AudioControlAdmission(storage.Store, storage.Store, new(storage.Tasks));
            var preferences = new MarkdownDisplaySettingsTests.Preferences { Value = MarkdownDisplayMode.Source };
            await using var markdown = new MarkdownDisplayConfigurationService(preferences, admission, new AuditLog(),
                storage.Time, NullLogger<MarkdownDisplayConfigurationService>.Instance);
            await markdown.ObserveAsync(storage.Token);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failing = false;
            var accessible = true;
            var sourceAvailable = true;
            var states = new List<DetailViewerState>();
            var windows = new List<DetailWindow>();
            var surface = new Window();
            using var details = new DetailWindowController(new Kora.Application.Documentation.EmbeddedUserDocumentationProvider(),
                () => accessible, new(NullLogger<NativeDetailRenderer>.Instance),
                new ReaderRetirementClipboard(), (state, result, copy) =>
                {
                    states.Add(state);
                    var window = new DetailWindow(state, result, copy);
                    windows.Add(window);
                    return window;
                }, NullLogger<DetailWindowController>.Instance, markdownDisplay: async (live, token) =>
                {
                    if (failing) { entered.SetResult(); await release.Task; }
                    return await markdown.CaptureAsync(live, token);
                });
            await using var composition = new Composition(storage, (content, live, resolve, token) =>
                details.OpenConversationDetailAsync(content, surface, live, async readToken =>
                {
                    if (!sourceAvailable) { throw new InvalidDataException("Original source withdrawn"); }
                    return await resolve(readToken);
                }, token), revokeDetails: details.RevokeConversationSession);
            var model = composition.Model;
            surface.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == storage.Request.SessionId));
                await model.ReadConversationAsync();
                var originalPage = model.ConversationMessages.ToArray();
                model.ComposerBody = "unsent original input";
                await model.OpenConversationDetailAsync(model.ConversationMessages.Single(row => row.Request == first.Request), assistant: true);
                var old = states.Single();
                old.SetSource(false);
                old.Search("marker").Should().BeTrue();
                var position = old.MatchStart;
                preferences.BeginWrite();
                failing = true;
                var pending = model.OpenConversationDetailAsync(model.ConversationMessages.Single(row => row.Request == second.Request), assistant: true);
                await entered.Task;
                states.Should().ContainSingle("the new source cannot publish while capture is pending");
                if (boundary is "source") { sourceAvailable = false; }
                if (boundary is "native") { accessible = false; }
                if (boundary is "privacy") { composition.Access.CanInspect = false; details.ClearForPrivacy(); }
                release.SetResult();
                await pending;
                await details.JoinPreferenceReadsAsync();
                states.Should().ContainSingle();
                model.ComposerBody.Should().Be(boundary is "privacy" ? string.Empty : "unsent original input");
                if (boundary is "live")
                {
                    model.ConversationStatus.Should().Contain("Markdown display preference unavailable").And.Contain("No new reader was published");
                    model.ConversationMessages.Should().Equal(originalPage);
                    old.Content.Should().NotBeNull();
                    old.IsSource.Should().BeFalse();
                    old.SearchQuery.Should().Be("marker");
                    old.MatchStart.Should().Be(position);
                }
                else
                {
                    model.ConversationStatus.Should().NotContain("Existing admitted readers");
                    model.ConversationMessages.Should().BeEmpty();
                    old.Content.Should().BeNull();
                }
                storage.Count("scoped_grants").Should().Be(0);
                storage.Count("host_questions").Should().Be(0);
            }
            finally
            {
                foreach (var window in windows) { window.Close(); }
                surface.Close();
            }
        });

    [Theory]
    [InlineData(MarkdownDisplayMode.Rendered)]
    [InlineData(MarkdownDisplayMode.Source)]
    public async Task NewFinalizedForegroundAnswerOpensExactPrimaryPaneWithoutChangingKeyboardFocus(MarkdownDisplayMode mode)
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
            await using var markdown = new MarkdownDisplayConfigurationService(new MarkdownDisplaySettingsTests.Preferences { Value = mode },
                admission, new AuditLog(), storage.Time, NullLogger<MarkdownDisplayConfigurationService>.Instance);
            await native.InitializeAsync(null, detailPresentationConfiguration: settings, markdownDisplayConfiguration: markdown);
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
            DetailWindowController? detailHost = null;
            ResponseWindow? window = null;
            await using var composition = new Composition(storage, (content, live, resolve, token) =>
                detailHost!.OpenConversationDetailAsync(content, window, live, resolve, token), adapter: adapter);
            var model = composition.Model;
            model.BindDetailPresentation(settings);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == storage.Request.SessionId));
            using var details = new DetailWindowController(native.Documents, native.Main,
                NullLogger<DetailWindowController>.Instance, NullLogger<NativeDetailRenderer>.Instance,
                NullLogger<StaticHtmlDetailRenderer>.Instance, markdown);
            detailHost = details;
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance, details);
            var owner = new SessionsPresentationOwner(model, native.Main);
            window = responses.PrepareSessions(model, owner, static () => { }, static () => { });
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
            automatic.Failures.Should().BeEmpty();
            automatic.Published.Should().Equal(true);
            window.SessionView.DetailsVisible.Should().BeTrue();
            var reader = window.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single();
            reader.FindControl<CheckBox>("SourceToggle")!.IsChecked.Should().Be(mode == MarkdownDisplayMode.Source);
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
            await model.ReadConversationAsync();
            message = model.ConversationMessages.Single();
            await model.OpenConversationDetailAsync(message, assistant: true);
            window.SessionView.DetailsVisible.Should().BeTrue("retained publication must complete: {0}", model.ConversationStatus);
            var retainedReader = window.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single();
            retainedReader.FindControl<CheckBox>("SourceToggle")!.IsChecked.Should().Be(mode == MarkdownDisplayMode.Source);
            retainedReader.FindControl<CheckBox>("SourceToggle")!.IsChecked = mode != MarkdownDisplayMode.Source;
            await model.OpenConversationDetailAsync(message, assistant: true);
            retainedReader.FindControl<CheckBox>("SourceToggle")!.IsChecked.Should().Be(mode != MarkdownDisplayMode.Source,
                "reopening the exact retained viewer must preserve its explicit mode");
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
            await using var markdown = new MarkdownDisplayConfigurationService(new MarkdownDisplaySettingsTests.Preferences(),
                admission, new AuditLog(), clock, NullLogger<MarkdownDisplayConfigurationService>.Instance);
            await native.InitializeAsync(null, detailPresentationConfiguration: settings, markdownDisplayConfiguration: markdown);
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
                NullLogger<StaticHtmlDetailRenderer>.Instance, markdown);
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
        internal List<Exception> Failures { get; } = [];
        public AutomaticDetailLease? CaptureAutomaticDetails()
        {
            var lease = actual.CaptureAutomaticDetails();
            Leases.Add(lease);
            return lease;
        }
        public async Task<bool> PublishAutomaticDetailsAsync(AutomaticDetailIntent intent)
        {
            IntentCurrent.Add(intent.IsCurrent());
            try
            {
                var published = await actual.PublishAutomaticDetailsAsync(intent);
                Published.Add(published);
                return published;
            }
            catch (Exception exception) { Failures.Add(exception); throw; }
        }
    }
}
