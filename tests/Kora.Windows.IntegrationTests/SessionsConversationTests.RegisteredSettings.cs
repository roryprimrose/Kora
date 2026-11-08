using System.Text;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RegisteredSettingsOrdinaryTurnDeadlineProductionControllerReviewsCancelsAndSeparatelyConfirms(bool compact, bool reset)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new HostTaskCoordinator(f.Tasks));
            var preferences = new LocalOrdinaryTurnDeadlinePreferences(f.Paths);
            preferences.Save(new(7));
            var audit = new AuditLog();
            var deadline = new OrdinaryTurnDeadlineConfigurationService(preferences, admission, audit);
            var running = deadline.Capture();
            var adapter = new DeadlineAnswerAdapter(reset ? "reset work.active-deadline-minutes" : "set work.active-deadline-minutes to 10");
            await using var composed = new Composition(f, adapter: adapter);
            var parent = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync(null, ordinaryDeadlineConfiguration: deadline);
            using var controller = new SessionsWindowController(native.Main, composed.Workspace, native.Evidence,
                composed.Access, NullLogger<SessionsViewModel>.Instance, conversation: composed.Conversation);
            Window? window = null;
            using var expanded = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) => window = view);
            using var small = Window.WindowOpenedEvent.AddClassHandler<CompactSessionWindow>((view, _) => window = view);
            if (compact) { controller.OpenCompact(); }
            else { controller.Open(); }
            var surface = window ?? throw new InvalidOperationException("The actual native deadline conversation did not open.");
            var model = (SessionsViewModel)surface.DataContext!;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(entry => entry.Authority.SessionId == f.Request.SessionId));
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest a longer future ordinary task allowance, without extending running work.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            adapter.Calls.Should().Be(1);
            preferences.Load().Should().Be(new OrdinaryTurnDeadline(7), "the model answer cannot write");
            model.ComposerBody = "Unsent text stays unchanged.";
            await ActivateOriginalRequestWindow(surface);
            surface.UpdateLayout();
            var button = surface.GetVisualDescendants().OfType<Button>().Single(item =>
                string.Equals(item.Name, "ReviewRegisteredSettingSuggestion", StringComparison.Ordinal));
            button.DataContext.Should().BeSameAs(answer);
            button.Focus().Should().BeTrue();
            await model.ReviewRegisteredSettingSuggestionAsync(answer);
            model.RegisteredSettingSuggestionDescription.Should().Contain("Current saved: 7 minutes")
                .And.Contain(reset ? "reset to default five minutes (not saved)" : "Proposed: 10 minutes")
                .And.Contain("shared device-local").And.Contain("future ordinary task dispatch only")
                .And.Contain("fixed provider/tool ceilings").And.Contain("Running turns are unchanged");
            model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue(model.ConversationStatus);
            preferences.Load().Should().Be(new OrdinaryTurnDeadline(7));
            audit.Events.Should().BeEmpty();
            surface.FindControl<Button>("CancelRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
            model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
            audit.Events.Should().BeEmpty();
            await model.ReviewRegisteredSettingSuggestionAsync(answer);
            await model.ConfirmRegisteredSettingSuggestionAsync();
            model.ConversationStatus.Should().Contain("saved with committed preference and audit readback")
                .And.Contain("Running turns").And.Contain("ceilings are unchanged");
            var expected = reset ? (OrdinaryTurnDeadline?)null : new(10);
            new LocalOrdinaryTurnDeadlinePreferences(f.Paths).Load().Should().Be(expected);
            deadline.Capture().Should().Be(expected ?? OrdinaryTurnDeadline.Default);
            running.Minutes.Should().Be(7, "the already captured running budget is immutable");
            audit.Events.Select(item => (item.ActionId, item.Initiator, item.Outcome)).Should().Equal(
                ("configuration.ordinary-turn-deadline", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Requested),
                ("configuration.ordinary-turn-deadline", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Succeeded));
            model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
            model.ComposerBody.Should().Be("Unsent text stays unchanged.");
            adapter.Calls.Should().Be(1, "review must reuse the exact retained answer, not regenerate it");
            (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token)).Should().Be(answer);
            f.Count("host_questions").Should().Be(0);
            f.Count("scoped_grants").Should().Be(0);
            surface.Close();
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("refresh")]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("close")]
    [InlineData("disk")]
    [InlineData("revision")]
    [InlineData("hide-reopen")]
    [InlineData("cancel-await")]
    [InlineData("cancel-confirm-await")]
    [InlineData("voice-laundering")]
    [InlineData("terminal-audit")]
    public async Task RegisteredSettingsOrdinaryTurnDeadlineRejectsStaleAwaitedAndUnconfirmedNativeReview(string transition)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            var preferences = new LocalOrdinaryTurnDeadlinePreferences(f.Paths);
            var audit = new DeadlineSuggestionAudit { FailSuccess = transition is "terminal-audit" };
            var deadline = new OrdinaryTurnDeadlineConfigurationService(preferences, admission, audit);
            using var policy = new CallCommunicationPolicy(new SummaryCallState());
            var adapter = new DeadlineAnswerAdapter("set work.active-deadline-minutes to 10");
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            model.BindOrdinaryTurnDeadlineSuggestions(async (message, eligible, validate, token) =>
            {
                async Task Validate(CancellationToken cancellation)
                {
                    reads++;
                    if (transition is "cancel-await" || transition is "cancel-confirm-await" && reads == 3)
                    { entered.TrySetResult(); await release.Task.WaitAsync(cancellation); }
                    await validate(cancellation);
                }
                return await OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(deadline, policy, f.Time, message, eligible, Validate, token);
            }, (review, token) => review.ConfirmAsync(token));
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest ten minutes for future ordinary work only.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            var owner = new SessionsPresentationOwner(model);
            var window = new SessionsWindow(model, owner);
            window.Show();
            await ActivateOriginalRequestWindow(window);
            try
            {
                await model.ReviewRegisteredSettingSuggestionAsync(answer with { });
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse("a copy is not the exact observed retained row");
                if (transition is "voice-laundering")
                {
                    using var voice = HostActivity.BeginRoot(new(new(Guid.NewGuid()), answer.Request.SessionId,
                        new(Guid.NewGuid()), RequestOrigin.ActivatedVoice), HostActivityLayer.Desktop, HostOperation.Request);
                    await model.ReviewRegisteredSettingSuggestionAsync(answer);
                }
                else
                {
                    var work = model.ReviewRegisteredSettingSuggestionAsync(answer);
                    if (transition is "cancel-confirm-await")
                    { await work; work = model.ConfirmRegisteredSettingSuggestionAsync(); }
                    if (transition is "cancel-await" or "cancel-confirm-await")
                    {
                        await Task.WhenAny(entered.Task, work);
                        entered.Task.IsCompleted.Should().BeTrue(model.ConversationStatus);
                        model.CanCancelRegisteredSettingSuggestion.Should().BeTrue();
                        window.FindControl<Button>("CancelRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        release.TrySetResult();
                    }
                    await work;
                }
                if (transition is "cancel") { model.CancelRegisteredSettingSuggestion(); }
                if (transition is "refresh") { await model.ReadConversationAsync(); }
                if (transition is "private") { composed.Access.CanInspect = false; }
                if (transition is "control") { composed.Access.ControlRevision++; }
                if (transition is "close") { model.Close(); }
                if (transition is "disk") { preferences.Save(new(7)); }
                if (transition is "revision") { await deadline.RefreshAsync(RequestOrigin.LocalUi, () => true, f.Token); }
                if (transition is "hide-reopen") { window.Hide(); window.Show(); }
                if (transition is "source")
                {
                    var request = new HostRequest(new(Guid.NewGuid()), answer.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
                    using var input = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
                    var preview = await f.Store.PreviewDispositionAsync(answer.Request.SessionId, answer.Generation, 0, f.Token);
                    await f.Tasks.CommitAsync(new(request, new(1), HostTaskState.IntentRecorded), 0, f.Token);
                    await f.Store.DisposeSessionAsync(request, preview, () => true, f.Token);
                }
                await model.ConfirmRegisteredSettingSuggestionAsync();
                audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                if (transition is "terminal-audit")
                {
                    preferences.ReadBack().Should().Be(new OrdinaryTurnDeadline(10));
                    FluentActions.Invoking(() => deadline.Capture()).Should().Throw<InvalidDataException>();
                    model.ConversationStatus.Should().Contain("save is unconfirmed").And.Contain("No automatic retry or rollback");
                }
                else { preferences.Load().Should().Be(transition is "disk" ? new OrdinaryTurnDeadline(7) : null); }
                adapter.Calls.Should().Be(1);
            }
            finally { owner.Revoke(); }
        });
    }

    private sealed class DeadlineAnswerAdapter(string text) : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            Encoding.UTF8.GetString(context.Span).Should().Contain("work.active-deadline-minutes")
                .And.Contain("No deadline undo").And.Contain("one inert suggestion");
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection, new(text, null)));
        }
    }

    private sealed class DeadlineSuggestionAudit : ISecurityAuditLog
    {
        internal bool FailSuccess { get; init; }
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent value)
        {
            HostActivity.RequireCurrent();
            if (FailSuccess && value.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("injected required audit failure"); }
            Events.Add(value);
        }
    }

    [Theory]
    [InlineData(false, "RegisteredSettingReviewReadback")]
    [InlineData(true, "RegisteredSettingReviewReadback")]
    [InlineData(false, "ConfirmRegisteredSettingSuggestion")]
    [InlineData(true, "CancelRegisteredSettingSuggestion")]
    public async Task RegisteredSettingKeyboardReviewReadsCancelsAndReturnsToExactSource(bool compact, string escapeControl)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var preferences = new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths),
                NullLogger<LocalAppearancePreferences>.Instance);
            var audit = new AuditLog();
            var appearance = new AppearanceConfigurationService(preferences, audit,
                NullLogger<AppearanceConfigurationService>.Instance);
            var adapter = new AppearanceAnswerAdapter("set appearance.theme to dark");
            await using var composed = new Composition(f, adapter: adapter, appearance: appearance);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest a dark theme.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            model.ComposerBody = "Unsent text must remain unchanged.";
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await ActivateOriginalRequestWindow(window);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Button Source() => window.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "ReviewRegisteredSettingSuggestion", StringComparison.Ordinal)
                    && ReferenceEquals(button.DataContext, answer));
                var source = Source();
                source.Focus().Should().BeTrue();
                source.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                Dispatcher.UIThread.RunJobs();
                var readback = window.FindControl<TextBlock>("RegisteredSettingReviewReadback")!;
                readback.IsFocused.Should().BeTrue();
                readback.Text.Should().Contain("current system (default); proposed dark mode").And.Contain("shared device-local");
                readback.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue("readback focus or Enter is not confirmation");
                preferences.LoadThemeMode().Should().BeNull();
                audit.Events.Should().BeEmpty();

                var composer = window.FindControl<TextBox>("ConversationComposer")!;
                composer.Focus().Should().BeTrue();
                var unrelatedKey = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F6 };
                composer.RaiseEvent(unrelatedKey);
                unrelatedKey.Handled.Should().BeFalse("settings review does not intercept unrelated composer keys");
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue();
                model.ComposerBody.Should().Be("Unsent text must remain unchanged.");
                var control = window.FindControl<Control>(escapeControl)!;
                control.Focus().Should().BeTrue();
                var escape = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
                control.RaiseEvent(escape);
                escape.Handled.Should().BeTrue();
                Dispatcher.UIThread.RunJobs();
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                model.ConversationStatus.Should().Contain("cancelled").And.Contain("No new setting write");
                source = Source();
                source.IsFocused.Should().BeTrue();
                model.ComposerBody.Should().Be("Unsent text must remain unchanged.");
                preferences.LoadThemeMode().Should().BeNull();
                audit.Events.Should().BeEmpty();

                source.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                window.FindControl<Button>("ConfirmRegisteredSettingSuggestion")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                preferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
                audit.Events.Select(item => item.Outcome).Should()
                    .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                source = Source();
                source.IsFocused.Should().BeTrue();
                adapter.Calls.Should().Be(1, "review recovery reuses the exact source without another model request");
                model.ComposerBody.Should().Be("Unsent text must remain unchanged.");
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);

                composer.Focus().Should().BeTrue();
                source.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                composer.IsFocused.Should().BeTrue("a review opened without source focus must not steal composer focus");
                window.FindControl<Button>("ConfirmRegisteredSettingSuggestion")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                composer.IsFocused.Should().BeTrue("completion must not steal focus moved outside the review");
                preferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
                model.ComposerBody.Should().Be("Unsent text must remain unchanged.");
                adapter.Calls.Should().Be(1);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RegisteredSettingAndModelMemoryNativeRoutesKeepSeparateConfirmationAndAdmission(bool compact, bool reset)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var preferences = new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths),
                NullLogger<LocalAppearancePreferences>.Instance);
            if (reset) { preferences.SaveThemeMode(ApplicationThemeMode.Light); }
            var audit = new AuditLog();
            var appearance = new AppearanceConfigurationService(preferences, audit,
                NullLogger<AppearanceConfigurationService>.Instance);
            var suggestion = reset ? "reset appearance.theme" : "set appearance.theme to dark";
            var expected = reset ? ApplicationThemeMode.System : ApplicationThemeMode.Dark;
            var adapter = new SettingAndMemoryAnswers(suggestion);
            await using var composed = new Composition(f, adapter: adapter, appearance: appearance, memoryContext: true);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest a dark theme.";
            await model.SubmitConversationAsync();
            model.ComposerBody = "Explicitly propose one session-memory preference.";
            await model.SubmitConversationAsync();
            await model.ReadConversationAsync();
            var setting = model.ConversationMessages.Single(item => string.Equals(item.AssistantResponse?.Body,
                suggestion, StringComparison.Ordinal));
            var memory = model.ConversationMessages.Single(item => string.Equals(item.AssistantResponse?.Body,
                MemoryProposal, StringComparison.Ordinal));
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                async Task Click(string name, ConversationMessage? source = null)
                {
                    if (source is not null)
                    {
                        window.FindControl<ListBox>("AnswerSourceRecords")!.ScrollIntoView(source);
                        window.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                    }
                    window.GetVisualDescendants().OfType<Button>()
                        .Single(item => string.Equals(item.Name, name, StringComparison.Ordinal)
                            && (source is null || ReferenceEquals(item.DataContext, source)))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.WhenAll(owner.RegisteredSettingOperation, owner.ModelMemoryOperation);
                }
                await Click("ReviewRegisteredSettingSuggestion", setting);
                model.RegisteredSettingSuggestionDescription.Should().Contain(reset ? "proposed system mode" : "proposed dark mode");
                if (reset) { model.RegisteredSettingSuggestionDescription.Should().Contain("Reset saves").And.Contain("saved preference"); }
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue();
                model.CanAcceptModelMemoryProposal.Should().BeFalse();
                preferences.LoadThemeMode().Should().Be(reset ? ApplicationThemeMode.Light : null);
                audit.Events.Should().BeEmpty();
                f.Count("reviewed_memory").Should().Be(0);
                await Click("ConfirmRegisteredSettingSuggestion");
                preferences.LoadThemeMode().Should().Be(expected);
                audit.Events.Select(item => item.Outcome).Should()
                    .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                f.Count("reviewed_memory").Should().Be(0, "setting confirmation cannot admit memory");
                await Click("ReviewModelMemoryProposal", memory);
                model.ModelMemoryProposalDescription.Should().Contain("Prefer short answers.").And.Contain("SESSION only");
                model.CanAcceptModelMemoryProposal.Should().BeTrue();
                model.CanAdmitModelMemoryProposal.Should().BeFalse();
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                await Click("AcceptModelMemoryProposal");
                f.Count("reviewed_memory").Should().Be(0, "memory acceptance still requires separate admission");
                await Click("AdmitModelMemoryProposal");
                model.ConversationStatus.Should().Contain("SESSION memory admitted");
                model.ModelMemoryProposalDescription.Should().BeEmpty();
                f.Count("reviewed_memory").Should().Be(1);
                preferences.LoadThemeMode().Should().Be(expected);
                audit.Events.Should().HaveCount(2, "memory admission cannot repeat a setting write");
                adapter.Calls.Should().Be(2, "neither review route regenerates an answer");
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RegisteredSettingReviewReportsPersistedButUnconfirmedWriteWithoutRetryOrRollback(bool presence, bool failRequested)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var preferences = new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths),
            NullLogger<LocalAppearancePreferences>.Instance);
        var audit = new UnconfirmedSettingAudit(failRequested);
        var appearance = new AppearanceConfigurationService(preferences, audit,
            NullLogger<AppearanceConfigurationService>.Instance);
        var adapter = new AppearanceAnswerAdapter(presence ? "set appearance.movement-speed to 50" : "set appearance.theme to dark");
        await using var composed = new Composition(f, adapter: adapter, appearance: appearance);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Suggest a dark theme.";
        await model.SubmitConversationAsync();
        await model.ReviewRegisteredSettingSuggestionAsync(model.ConversationMessages.Single());
        await model.ConfirmRegisteredSettingSuggestionAsync();
        if (presence)
        {
            preferences.LoadPresenceMovementSpeedPercent().Should().Be(failRequested ? null : 50,
                "a failed requested receipt prevents writes, but a failed terminal receipt cannot promise rollback");
            preferences.LoadThemeMode().Should().BeNull();
        }
        else { preferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark, "the failed terminal receipt cannot promise rollback"); }
        model.ConversationStatus.Should().Contain("save is unconfirmed").And.Contain("a write may have persisted")
            .And.Contain("No automatic retry or rollback");
        model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
        model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
        audit.Events.Should().HaveCount(failRequested ? 0 : 1);
        audit.Events.Should().OnlyContain(item => item.Outcome == SecurityAuditOutcome.Requested);
        await model.ConfirmRegisteredSettingSuggestionAsync();
        audit.Events.Should().HaveCount(failRequested ? 0 : 1, "a consumed review cannot retry an uncertain save");
        adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RegisteredSettingReviewReplacesTheExactFamilyWithoutKeepingOldConfirmationAuthority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var audit = new AuditLog();
        var appearance = new AppearanceConfigurationService(
            new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths), NullLogger<LocalAppearancePreferences>.Instance),
            audit, NullLogger<AppearanceConfigurationService>.Instance);
        var preferences = new LocalTextToSpeechPreferences(f.Paths, NullLogger<LocalTextToSpeechPreferences>.Instance);
        var speech = new SpeechConfigurationService(preferences, new SummaryCatalog(), audit,
            NullLogger<SpeechConfigurationService>.Instance);
        using var policy = new CallCommunicationPolicy(new SummaryCallState());
        var adapter = new SettingAnswers();
        await using var composed = new Composition(f, adapter: adapter, appearance: appearance, speech: speech, speechPolicy: policy);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Suggest a theme.";
        await model.SubmitConversationAsync();
        model.ComposerBody = "Suggest a future speech cap.";
        await model.SubmitConversationAsync();
        await model.ReadConversationAsync();
        var first = model.ConversationMessages.Single(item => string.Equals(item.AssistantResponse?.Body,
            "set appearance.theme to dark", StringComparison.Ordinal));
        var second = model.ConversationMessages.Single(item => string.Equals(item.AssistantResponse?.Body,
            "set speech.summary-words to 40", StringComparison.Ordinal));
        await model.ReviewRegisteredSettingSuggestionAsync(first);
        model.CanConfirmAppearanceSuggestion.Should().BeTrue();
        await model.ReviewRegisteredSettingSuggestionAsync(second);
        model.CanConfirmAppearanceSuggestion.Should().BeFalse();
        model.AppearanceSuggestionDescription.Should().BeEmpty();
        model.CanConfirmSpeechSummarySuggestion.Should().BeTrue();
        model.RegisteredSettingSuggestionDescription.Should().Contain("Proposed: 3 sentences, 40 words");
        audit.Events.Should().BeEmpty();
        await model.ConfirmRegisteredSettingSuggestionAsync();
        appearance.Get(AppearanceOption.Theme).Value.Should().Be(new AppearanceValue.Theme(ApplicationThemeMode.System));
        preferences.LoadSummaryLimits().Should().Be(new SpokenSummaryLimits(3, 40));
        model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
        model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
        audit.Events.Should().HaveCount(2);
        await model.ConfirmRegisteredSettingSuggestionAsync();
        audit.Events.Should().HaveCount(2, "confirmation cannot be replayed");
        adapter.Calls.Should().Be(2, "review never regenerates an answer");
    }

    private sealed class SettingAndMemoryAnswers(string setting = "set appearance.theme to dark") : IModelTurnAdapter
    {
        internal int Calls { get; private set; }

        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            Encoding.UTF8.GetString(context.Span).Should().Contain("Registered Kora setting suggestions are inert data")
                .And.Contain("responses.default-mode").And.Contain("Hybrid/VoiceOnly/VisualOnly")
                .And.Contain("including VoiceOnly")
                .And.Contain("explicitly requests a session-memory proposal");
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(Calls == 1 ? setting : MemoryProposal, null)));
        }
    }

    private sealed class SettingAnswers : IModelTurnAdapter
    {
        internal int Calls { get; private set; }

        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(Calls == 1 ? "set appearance.theme to dark" : "set speech.summary-words to 40", null)));
        }

    }

    private sealed class UnconfirmedSettingAudit(bool failRequested = false) : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];

        public void Write(SecurityAuditEvent auditEvent)
        {
            if (failRequested || auditEvent.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("Terminal setting audit unavailable."); }
            Events.Add(auditEvent);
        }
    }
}
