using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionRoutingTests
{
    [Theory]
    [InlineData("send")]
    [InlineData("cancel")]
    [InlineData("send please")]
    [InlineData("native")]
    public async Task OptedInWakeReviewOpensOneSendReplyWithoutFocusAndJoinsBeforeSubmission(string answer)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var capture = new RoutingReplyCapture();
            await using var f = new WakeNativeComposition(capture);
            var speech = new SendReviewSpeech();
            f.Composition.Ui.Native.SpeechOverride = speech;
            await f.Initialize(expanded: true);
            var native = f.Composition.Ui.Native;
            await using var settingsAdmission = new Kora.Application.Voice.AudioControlAdmission(
                native.Interactions, native.Interactions, new(native.Tasks));
            var preferences = new LocalSpokenSendReplyPreferences(native);
            preferences.Save(true);
            native.Main.BindSpokenSendReplyConfiguration(new(preferences, settingsAdmission, native.Audit));
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => opening.TrySetResult(window));
            var original = await f.Stage.NextAsync();
            original.Wake();
            await original.Capture.Started.Task;
            original.Capture.Transcript("compare exact original plans");
            original.Capture.Done.SetResult();
            var window = await AwaitWakeOpening(f.Voice, opening.Task);
            window.IsActive.Should().BeFalse();
            native.Main.IsSpeechResponseEnabled.Should().BeTrue();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var status = Find<TextBlock>(window, "RoutingVoiceStatus");
            status.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBlock.TextProperty && status.Text?.StartsWith("Speak exactly", StringComparison.Ordinal) == true)
                { ready.TrySetResult(); }
            };
            await Choose(window);
            await speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            capture.Opens.Should().Be(0, "playback must complete before capture");
            speech.Text.Should().Be("Send to Alpha: compare exact original plans?");
            speech.Release.SetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            capture.Opens.Should().Be(1, Find<TextBlock>(window, "RoutingVoiceStatus").Text);
            capture.IsListening.Should().BeTrue();
            f.Composition.Adapter.Calls.Should().Be(0);
            capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (answer is "native") { Click(window, "RoutingSubmit"); }
            else { capture.Transcript(answer); }
            await capture.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            f.Composition.Adapter.Calls.Should().Be(0, "a transcript or native gesture is not a capture release receipt");
            capture.Closing.SetResult();
            await window.VoiceOperation.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            var successor = f.Stage.NextAsync();
            await Task.WhenAny(successor, f.Voice.Completion).WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            if (f.Voice.Completion.IsCompleted) { await f.Voice.Completion; }
            capture.Opens.Should().Be(1);
            capture.IsCaptureQuiescent.Should().BeTrue();
            f.Composition.Adapter.Calls.Should().Be(answer is "send" or "native" ? 1 : 0);
            if (answer is "send" or "native")
            {
                f.Composition.Adapter.Originals.Should().Equal("compare exact original plans");
                f.Composition.Adapter.Requests.Single().Origin.Should().Be(RequestOrigin.ActivatedVoice);
            }
            await native.Main.DisableListeningFromTrayAsync();
            await f.Voice.Completion;
        });
    }

    [Fact]
    public async Task OptedInTypedReviewStillRequiresExplicitNativeOrVoiceGesture()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var capture = new RoutingReplyCapture();
            await using var f = new LocalComposition(syntheticVoice: true);
            await f.Initialize(expanded: true, voiceRecognition: capture);
            var native = f.Ui.Native;
            await using var settingsAdmission = new AudioControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var preferences = new LocalSpokenSendReplyPreferences(native);
            preferences.Save(true);
            native.Main.BindSpokenSendReplyConfiguration(new(preferences, settingsAdmission, native.Audit));
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => opening.TrySetResult(window));
            native.Main.CommandText = "Explain original typed plan";
            var operation = native.Main.RunTypedCommand.ExecuteAsync();
            var window = await AwaitVoiceOpening(operation, opening.Task);
            await Choose(window);
            capture.Opens.Should().Be(0);
            Find<Button>(window, "RoutingSubmit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await operation;
            Dispatcher.UIThread.RunJobs();
            f.Adapter.Requests.Last().Origin.Should().Be(RequestOrigin.LocalUi);
        });
    }

    private sealed class SendReviewSpeech : ITextToSpeechService
    {
        private static readonly SpeechVoice Voice = new("fixture.send", "Synthetic send readback", "en-US", SpeechVoiceGender.Female);
        private static readonly AudioOutputDevice Output = new("fixture.send", "Synthetic output - no audio endpoint");
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string? Text { get; private set; }
        public bool IsSpeaking { get; private set; }
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() { }
        public IReadOnlyList<SpeechProvider> GetProviders() =>
            [new(SpeechProviderIds.Windows, "Synthetic speech", "No installed engine.", true, true, null, Voice.Id)];
        public IReadOnlyList<SpeechVoice> GetVoices() => [Voice];
        public SpeechVoice GetDefaultVoice() => Voice;
        public IReadOnlyList<AudioOutputDevice> GetOutputDevices() => [Output];
        public AudioOutputDevice GetDefaultOutputDevice() => Output;
        public async Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default)
        {
            Text = text;
            IsSpeaking = true;
            Started.TrySetResult();
            try { await Release.Task.WaitAsync(cancellationToken); }
            finally { IsSpeaking = false; }
        }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InstallProviderAsync(string providerId, IProgress<SpeechProviderInstallProgress> progress, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic fixture cannot install speech.");
        public Task RemoveProviderAsync(string providerId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic fixture cannot remove speech.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
