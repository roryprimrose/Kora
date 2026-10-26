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

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 30)]
    [InlineData(true, 100)]
    public async Task VolumeSuggestionExpandedAndCompactReviewExactAnswerAndCommitOneAuditedPreference(bool compact, int percent)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new HostTaskCoordinator(f.Tasks));
            var preferences = new LocalPlaybackVolumePreferences(f.Paths);
            var playback = new VolumeSuggestionPlayback();
            var audit = new AuditLog();
            var volume = new PlaybackVolumeConfigurationService(preferences, playback, admission, audit);
            volume.Observe();
            using var policy = new CallCommunicationPolicy(new SummaryCallState());
            var adapter = new SummaryAnswerAdapter("set speech.playback-volume to " + percent.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await using var composed = new Composition(f, adapter: adapter, speechPolicy: policy, volume: volume);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest a quieter Kora-only playback volume.";
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
                model.RegisteredSettingSuggestionDescription.Should().Be(model.PlaybackVolumeSuggestionDescription);
                model.PlaybackVolumeSuggestionDescription.Should().Contain("Current desired: 100%; effective: 100% (default)")
                    .And.Contain($"Proposed: {percent}%").And.Contain("shared device-local").And.Contain("never synthesized or replayed");
                preferences.Load().Should().BeNull();
                audit.Events.Should().BeEmpty();
                window.FindControl<Button>("ConfirmRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.ConversationStatus.Should().Contain("saved with committed preference and audit readback");
                new LocalPlaybackVolumePreferences(f.Paths).Load().Should().Be(new PlaybackVolume(percent));
                playback.Volume.Should().Be(new PlaybackVolume(percent));
                audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                model.PlaybackVolumeSuggestionDescription.Should().BeEmpty();
                model.CanConfirmPlaybackVolumeSuggestion.Should().BeFalse();
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
    public async Task VolumeSuggestionNativeLifetimeRejectsCopiesAndLateWrites(string transition)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var admission = new AudioControlAdmission(f.Store, f.Store, new HostTaskCoordinator(f.Tasks));
        var preferences = new LocalPlaybackVolumePreferences(f.Paths);
        var volume = new PlaybackVolumeConfigurationService(preferences, new VolumeSuggestionPlayback(), admission, new AuditLog());
        volume.Observe();
        using var policy = new CallCommunicationPolicy(new SummaryCallState());
        await using var composed = new Composition(f, adapter: new SummaryAnswerAdapter("set speech.playback-volume to 30"),
            speechPolicy: policy, volume: volume);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Suggest quieter Kora speech.";
        await model.SubmitConversationAsync();
        var answer = model.ConversationMessages.Single();
        await model.ReviewPlaybackVolumeSuggestionAsync(answer with { });
        model.CanConfirmPlaybackVolumeSuggestion.Should().BeFalse();
        await model.ReviewPlaybackVolumeSuggestionAsync(answer);
        model.CanConfirmPlaybackVolumeSuggestion.Should().BeTrue();
        if (transition is "cancel") { model.CancelPlaybackVolumeSuggestion(); }
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
        if (transition is "settings") { preferences.Save(new(50)); volume.Observe(); }
        await model.ConfirmPlaybackVolumeSuggestionAsync();
        preferences.Load().Should().Be(transition is "settings" ? new PlaybackVolume(50) : null);
    }

    private sealed class VolumeSuggestionPlayback : ISpeechPlaybackService, IPlaybackVolumeControl
    {
        internal PlaybackVolume? Volume { get; private set; }
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() { }
        public void SetPlaybackVolume(PlaybackVolume? volume) => Volume = volume;
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Suggestion review and confirmation must never synthesize or replay audio.");
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
