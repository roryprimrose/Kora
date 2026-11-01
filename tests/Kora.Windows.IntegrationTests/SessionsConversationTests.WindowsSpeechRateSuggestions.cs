using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowsRateExpandedAndCompactSelectExactRetainedAnswerThenConfirmThroughExistingWriter(bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new HostTaskCoordinator(f.Tasks));
            var preferences = new LocalWindowsSpeechRatePreferences(f.Paths);
            var playback = new RateSuggestionPlayback();
            var audit = new AuditLog();
            var speech = new SpeechConfigurationService(new RateSelection(), playback, audit, NullLogger<SpeechConfigurationService>.Instance);
            speech.Reload();
            var rate = new WindowsSpeechRateConfigurationService(preferences, playback, speech, admission, audit,
                NullLogger<WindowsSpeechRateConfigurationService>.Instance);
            rate.Observe();
            using var policy = new CallCommunicationPolicy(new SummaryCallState());
            var adapter = new SummaryAnswerAdapter("set speech.windows-rate to -1");
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            model.BindWindowsSpeechRateSuggestions((message, eligible, validate, token) =>
                WindowsSpeechRateSuggestionReview.ReviewAsync(rate, policy, f.Time, message, eligible, validate, token),
                (review, token) => review.ConfirmAsync(token));
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest slower Windows speech.";
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
                model.RegisteredSettingSuggestionDescription.Should().Contain("Current desired: 0; effective: 0 (default)")
                    .And.Contain("Proposed: -1").And.Contain("WindowsNative").And.Contain("Kokoro unchanged");
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue();
                preferences.Load().Should().BeNull();
                audit.Events.Should().BeEmpty();
                window.FindControl<Button>("ConfirmRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.ConversationStatus.Should().Contain("saved with committed preference and audit readback");
                new LocalWindowsSpeechRatePreferences(f.Paths).Load().Should().Be(new WindowsSpeechRate(-1));
                playback.Rate.Should().Be(new WindowsSpeechRate(-1));
                audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                adapter.Calls.Should().Be(1);
                (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token)).Should().Be(answer);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
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
    [InlineData("provider")]
    [InlineData("write")]
    public async Task WindowsRateNativeReviewRejectsCopiesAndLateSourceOrAuthorityChanges(string transition)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var admission = new AudioControlAdmission(f.Store, f.Store, new HostTaskCoordinator(f.Tasks));
        var preferences = new LocalWindowsSpeechRatePreferences(f.Paths);
        var playback = new RateSuggestionPlayback();
        var audit = new AuditLog();
        var speech = new SpeechConfigurationService(new RateSelection(), playback, audit, NullLogger<SpeechConfigurationService>.Instance);
        speech.Reload();
        var rate = new WindowsSpeechRateConfigurationService(preferences, playback, speech, admission, audit,
            NullLogger<WindowsSpeechRateConfigurationService>.Instance);
        rate.Observe();
        using var policy = new CallCommunicationPolicy(new SummaryCallState());
        await using var composed = new Composition(f, adapter: new SummaryAnswerAdapter("set speech.windows-rate to -1"));
        var model = composed.Model;
        model.BindWindowsSpeechRateSuggestions((message, eligible, validate, token) =>
            WindowsSpeechRateSuggestionReview.ReviewAsync(rate, policy, f.Time, message, eligible, validate, token),
            (review, token) => review.ConfirmAsync(token));
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Suggest slower Windows speech.";
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
        if (transition is "settings") { preferences.Save(new(5)); rate.Observe(); }
        if (transition is "provider") { playback.Supported = false; }
        if (transition is "write")
        {
            audit.Events.Clear();
            preferences.BeginWrite();
        }
        await model.ConfirmRegisteredSettingSuggestionAsync();
        preferences.ReadBack().Should().Be(transition is "settings" ? new WindowsSpeechRate(5) : null);
        audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
    }

    private sealed class RateSelection : ITextToSpeechPreferences
    {
        public SpeechSelection? LoadSelection() => new(SpeechProviderIds.Windows, null);
        public string? LoadProviderId() => SpeechProviderIds.Windows;
        public string? LoadVoiceId() => null;
        public SpokenSummaryLimits? LoadSummaryLimits() => null;
        public void SaveSummaryLimits(SpokenSummaryLimits limits) => throw new NotSupportedException();
        public void ResetSummaryLimits() => throw new NotSupportedException();
        public void SaveSelection(SpeechSelection selection) => throw new NotSupportedException();
        public void SaveProviderId(string providerId) => throw new NotSupportedException();
        public void SaveVoiceId(string voiceId) => throw new NotSupportedException();
    }

    private sealed class RateSuggestionPlayback : ISpeechPlaybackService, IWindowsSpeechRateControl, ISpeechCatalog
    {
        internal WindowsSpeechRate? Rate { get; private set; }
        internal bool Supported { get; set; } = true;
        public IReadOnlyList<SpeechProvider> GetProviders() =>
            [new(SpeechProviderIds.Windows, "Windows", "Native", true, true, null, "default")
                { RateSupport = Supported ? SpeechRateSupport.WindowsNative : SpeechRateSupport.Unsupported }];
        public IReadOnlyList<SpeechVoice> GetVoices() => [new("default", "Windows", "en-US", SpeechVoiceGender.Female)];
        public SpeechVoice? GetDefaultVoice() => GetVoices()[0];
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() { }
        public void SetWindowsSpeechRate(WindowsSpeechRate? rate) => Rate = rate;
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Setting review must never synthesize or replay audio.");
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
