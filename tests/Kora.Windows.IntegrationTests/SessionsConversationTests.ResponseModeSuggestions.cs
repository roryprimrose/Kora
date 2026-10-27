using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseModeSuggestionProductionControllerBindsBothNativeSizes(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var fixture = new NativeUxFixtureSession(parent);
            await fixture.InitializeAsync();
            await using var conversation = new SessionConversationService(fixture.Interactions, fixture.Interactions, fixture.Sessions,
                fixture.Access, new NoResponseModeModelAdmission(), TimeProvider.System,
                NullLogger<SessionConversationService>.Instance);
            using var controller = new SessionsWindowController(fixture.Main, fixture.Sessions, fixture.Evidence,
                fixture.Access, NullLogger<SessionsViewModel>.Instance, conversation: conversation);
            SessionsViewModel? model = null;
            using var expandedOpened = Window.WindowOpenedEvent.AddClassHandler<SessionsWindow>((view, _) =>
                model = (SessionsViewModel)view.DataContext!);
            using var compactOpened = Window.WindowOpenedEvent.AddClassHandler<CompactSessionWindow>((view, _) =>
                model = (SessionsViewModel)view.DataContext!);
            if (compact) { controller.OpenCompact(); }
            else { controller.Open(); }
            var state = model ?? throw new InvalidOperationException("The actual native conversation did not open.");
            await state.RefreshAsync();
            await state.SelectAsync(state.Sessions.Single(entry =>
                entry.Authority.SessionId == fixture.LifecycleTarget!.SessionId));
            await state.ReadConversationAsync();
            state.CanReviewResponseModeSuggestion.Should().BeTrue("the production controller must bind the shared retained-setting review route");
        });
    }

    private sealed class NoResponseModeModelAdmission : IModelTurnAdmission
    {
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Controller composition must not execute a model.");
    }

    [Theory]
    [InlineData(false, ResponseOutputMode.Hybrid)]
    [InlineData(true, ResponseOutputMode.Hybrid)]
    [InlineData(false, ResponseOutputMode.VoiceOnly)]
    [InlineData(true, ResponseOutputMode.VoiceOnly)]
    [InlineData(false, ResponseOutputMode.VisualOnly)]
    [InlineData(true, ResponseOutputMode.VisualOnly)]
    public async Task ResponseModeSuggestionBothNativeSizesReviewConfirmAndReadBackExactMode(bool compact, ResponseOutputMode mode)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new HostTaskCoordinator(f.Tasks));
            var preferences = new LocalResponseOutputPreferences(f.Paths, NullLogger<LocalResponseOutputPreferences>.Instance);
            var playback = new VolumeSuggestionPlayback();
            var audit = new AuditLog();
            var service = new ResponseModeConfigurationService(preferences, admission, audit, playback);
            service.Observe();
            using var policy = new CallCommunicationPolicy(new SummaryCallState());
            var adapter = new SummaryAnswerAdapter($"set responses.default-mode to {mode}");
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            model.BindResponseModeSuggestions((message, live, validate, token) =>
                ResponseModeSuggestionReview.ReviewAsync(service, policy, f.Time, message, live, validate, token),
                (review, token) => review.ConfirmAsync(token));
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Recommend whether to show or speak future responses.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                var button = window.GetVisualDescendants().OfType<Button>()
                    .Single(item => string.Equals(item.Name, "ReviewRegisteredSettingSuggestion", StringComparison.Ordinal));
                button.DataContext.Should().BeSameAs(answer);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.RegisteredSettingsDescription.Should().Contain("responses.default-mode: Hybrid/VoiceOnly/VisualOnly");
                model.RegisteredSettingSuggestionDescription.Should().Be(model.ResponseModeSuggestionDescription)
                    .And.Contain("Current saved: unsaved (default)").And.Contain("configured effective: Hybrid")
                    .And.Contain($"Proposed: {mode}").And.Contain("shared device-local").And.Contain("NEXT eligible response")
                    .And.Contain("Mandatory full visual").And.Contain("even in VoiceOnly").And.Contain("Privacy and call");
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue();
                preferences.LoadDefaultMode().Should().BeNull();
                audit.Events.Should().BeEmpty();
                window.FindControl<Button>("ConfirmRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.ConversationStatus.Should().Contain("saved with committed preference and audit readback")
                    .And.Contain("mandatory full visual, privacy and call exceptions");
                new LocalResponseOutputPreferences(f.Paths, NullLogger<LocalResponseOutputPreferences>.Instance)
                    .LoadDefaultMode().Should().Be(mode);
                service.Get().Saved.Should().Be(mode);
                service.Get().Effective.Should().Be(mode);
                audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                await model.ConfirmRegisteredSettingSuggestionAsync();
                audit.Events.Should().HaveCount(2, "duplicate confirmation cannot write again");
                adapter.Calls.Should().Be(1, "review and confirmation never re-execute the retained model");
                (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token)).Should().Be(answer);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                playback.Volume.Should().BeNull("response mode is not volume control");
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("refresh")]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("close")]
    [InlineData("settings")]
    [InlineData("expiry")]
    [InlineData("rebind")]
    public async Task ResponseModeSuggestionNativeLifetimeRetiresExactReviewWithoutLateWrite(string transition)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var admission = new AudioControlAdmission(f.Store, f.Store, new HostTaskCoordinator(f.Tasks));
        var preferences = new LocalResponseOutputPreferences(f.Paths, NullLogger<LocalResponseOutputPreferences>.Instance);
        var audit = new AuditLog();
        var service = new ResponseModeConfigurationService(preferences, admission, audit, new VolumeSuggestionPlayback());
        service.Observe();
        using var policy = new CallCommunicationPolicy(new SummaryCallState());
        await using var composed = new Composition(f, adapter: new SummaryAnswerAdapter("set responses.default-mode to VoiceOnly"));
        var model = composed.Model;
        void Bind() => model.BindResponseModeSuggestions((message, live, validate, token) =>
            ResponseModeSuggestionReview.ReviewAsync(service, policy, f.Time, message, live, validate, token),
            (review, token) => review.ConfirmAsync(token));
        Bind();
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Recommend future response output.";
        await model.SubmitConversationAsync();
        var answer = model.ConversationMessages.Single();
        await model.ReviewRegisteredSettingSuggestionAsync(answer with { });
        model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
        await model.ReviewRegisteredSettingSuggestionAsync(answer);
        model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue();
        if (transition is "cancel") { model.CancelRegisteredSettingSuggestion(); }
        if (transition is "refresh") { await model.ReadConversationAsync(); }
        if (transition is "source")
        {
            var request = new HostRequest(new(Guid.NewGuid()), answer.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            using var input = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
            var preview = await f.Store.PreviewDispositionAsync(answer.Request.SessionId, answer.Generation, 0, f.Token);
            await f.Tasks.CommitAsync(new(request, new(1), HostTaskState.IntentRecorded), 0, f.Token);
            await f.Store.DisposeSessionAsync(request, preview, () => true, f.Token);
        }
        if (transition is "private") { composed.Access.CanInspect = false; }
        if (transition is "close") { model.Close(); }
        if (transition is "settings") { preferences.SaveDefaultMode(ResponseOutputMode.VisualOnly); service.Observe(); }
        if (transition is "expiry") { f.Time.Now = f.Time.Now.AddSeconds(60); }
        if (transition is "rebind") { Bind(); }
        await model.ConfirmRegisteredSettingSuggestionAsync();
        preferences.LoadDefaultMode().Should().Be(transition is "settings" ? ResponseOutputMode.VisualOnly : null);
        audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task ResponseModeSuggestionNativeUnconfirmedAuditDoesNotClaimRollbackOrRetry()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var admission = new AudioControlAdmission(f.Store, f.Store, new HostTaskCoordinator(f.Tasks));
        var preferences = new LocalResponseOutputPreferences(f.Paths, NullLogger<LocalResponseOutputPreferences>.Instance);
        var audit = new UnconfirmedSettingAudit();
        var service = new ResponseModeConfigurationService(preferences, admission, audit, new VolumeSuggestionPlayback());
        service.Observe();
        using var policy = new CallCommunicationPolicy(new SummaryCallState());
        var adapter = new SummaryAnswerAdapter("set responses.default-mode to VoiceOnly");
        await using var composed = new Composition(f, adapter: adapter);
        var model = composed.Model;
        model.BindResponseModeSuggestions((message, live, validate, token) =>
            ResponseModeSuggestionReview.ReviewAsync(service, policy, f.Time, message, live, validate, token),
            (review, token) => review.ConfirmAsync(token));
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Recommend future response output.";
        await model.SubmitConversationAsync();
        await model.ReviewRegisteredSettingSuggestionAsync(model.ConversationMessages.Single());
        await model.ConfirmRegisteredSettingSuggestionAsync();
        preferences.ReadBackDefaultMode().Should().Be(ResponseOutputMode.VoiceOnly);
        preferences.Invoking(item => item.LoadDefaultMode()).Should().Throw<InvalidDataException>();
        model.ConversationStatus.Should().Contain("save is unconfirmed").And.Contain("a write may have persisted")
            .And.Contain("No automatic retry or rollback");
        model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
        model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
        audit.Events.Should().ContainSingle().Which.Outcome.Should().Be(SecurityAuditOutcome.Requested);
        await model.ConfirmRegisteredSettingSuggestionAsync();
        audit.Events.Should().ContainSingle();
        adapter.Calls.Should().Be(1);
    }
}
