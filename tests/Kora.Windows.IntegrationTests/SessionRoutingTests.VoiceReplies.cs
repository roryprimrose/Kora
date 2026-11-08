using System.Globalization;

using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Hosting.SessionRouting;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionRoutingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExplicitSpokenChoiceAcknowledgesThenSeparatelySubmitsUnchangedNativeOriginal(bool expanded, bool spokenConfirmation)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var capture = new RoutingReplyCapture();
            await using var f = new LocalComposition(syntheticVoice: true);
            await f.Initialize(expanded, voiceRecognition: capture);
            var main = f.Ui.Native.Main;
            main.BindSessionRoutingCaptureRelease(HandsFreeVoiceContinuation.CaptureRelease.Preserved);
            main.BindVoiceOwnershipGate(static () => true);
            await main.SetVoiceConsentAsync(true);
            main.IsVoiceEnabled.Should().BeTrue();
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => opening.TrySetResult(window));
            main.CommandText = "Explain this original design";
            var operation = main.RunTypedCommand.ExecuteAsync();
            var window = await AwaitVoiceOpening(operation, opening.Task);
            capture.Opens.Should().Be(0);
            await SpeakChoice(window, capture, "1");
            Find<TextBlock>(window, "RoutingAcknowledgment").Text.Should().StartWith("Destination: Alpha.");
            f.Adapter.Calls.Should().Be(1, "choice and acknowledgment are not original-request submission");
            capture.Opens.Should().Be(1, "acknowledgment must not automatically open confirmation capture");
            if (spokenConfirmation) { await SpeakChoice(window, capture, "Submit original request"); }
            else { Click(window, "RoutingSubmit"); }
            await operation;
            f.Adapter.Calls.Should().Be(2);
            f.Adapter.Originals.Should().Equal("Explain this original design", "Explain this original design");
            f.Adapter.Requests[1].Origin.Should().Be(RequestOrigin.LocalUi);
            var page = await f.Ui.Native.Interactions.ReadConversationPageAsync(f.Ui.First.Authority.SessionId,
                null, 50, f.Ui.Native.Token);
            page.Messages.Should().ContainSingle().Which.AssistantResponse!.Body.Should().Be("Genuine second-turn answer");
            capture.IsCaptureQuiescent.Should().BeTrue();
            window.IsVisible.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeWakeHandoverCorrectsAndSubmitsTheExactOriginalVoiceChannel(bool fresh)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var capture = new RoutingReplyCapture();
            await using var f = new WakeNativeComposition(capture);
            await f.Initialize(expanded: true);
            var native = f.Composition.Ui.Native;
            var adviceOpening = new TaskCompletionSource<SessionRoutingAdviceWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            var routeOpening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var adviceOpened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingAdviceWindow>((window, _) => adviceOpening.TrySetResult(window));
            using var routeOpened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => routeOpening.TrySetResult(window));
            var original = await f.Stage.NextAsync();
            original.Wake();
            await original.Capture.Started.Task;
            original.Capture.Transcript("compare these original plans");
            original.Capture.Done.SetResult();
            var advice = await AwaitWakeOpening(f.Voice, adviceOpening.Task);
            await ActivateRoutingWindow(advice, f.Composition.Ui.Window);
            ClickAdvice(advice, "RoutingAdviceAllow");
            var window = await AwaitWakeOpening(f.Voice, routeOpening.Task);
            await ActivateRoutingWindow(window, f.Composition.Ui.Window);
            await SpeakChoice(window, capture, "1");
            Find<TextBlock>(window, "RoutingAcknowledgment").Text.Should().StartWith("Destination: Alpha.");
            await SpeakChoice(window, capture, "Correct destination");
            Find<Button>(window, "RoutingSubmit").IsEnabled.Should().BeFalse();
            var inventory = Find<ComboBox>(window, "RoutingDestinations");
            var ordinal = Array.FindIndex(inventory.Items.Cast<object>().ToArray(),
                item => item.ToString()!.EndsWith(": Beta", StringComparison.Ordinal)) + 1;
            await SpeakChoice(window, capture, fresh ? "New" : ordinal.ToString(CultureInfo.InvariantCulture));
            Find<TextBlock>(window, "RoutingAcknowledgment").Text.Should().StartWith(fresh ? "Destination: New conversation" : "Destination: Beta.");
            capture.Opens.Should().Be(3);
            f.Stage.Opens.Should().Be(1, "the explicit reply path never starts another ambient capture");
            var terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Presented(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (args.PropertyName is nameof(native.Main.ResponseBody)
                    && native.Main.ResponseBody.Contains("Genuine second-turn answer", StringComparison.Ordinal))
                { terminal.TrySetResult(); }
            }
            native.Main.PropertyChanged += Presented;
            await SpeakChoice(window, capture, "Submit original request");
            try { await terminal.Task.WaitAsync(TestContext.Current.CancellationToken); }
            finally { native.Main.PropertyChanged -= Presented; }
            await f.Voice.StopAsync(TestContext.Current.CancellationToken);
            await f.Voice.Completion;
            f.Diagnostics.Failure.Should().BeNull();
            f.Composition.Adapter.Calls.Should().Be(2);
            f.Composition.Adapter.Originals.Should().Equal("compare these original plans", "compare these original plans");
            f.Composition.Adapter.Requests[1].Origin.Should().Be(RequestOrigin.ActivatedVoice);
            if (!fresh) { f.Composition.Adapter.Requests[1].SessionId.Should().Be(f.Composition.Ui.Second.Authority.SessionId); }
            capture.Opens.Should().Be(4);
            capture.IsCaptureQuiescent.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("shutdown")]
    [InlineData("invalid")]
    [InlineData("source")]
    public async Task RoutingVoiceWithdrawalJoinsCaptureAndNeverSubmitsLateText(string reason)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var capture = new RoutingReplyCapture();
            await using var f = new LocalComposition(syntheticVoice: true);
            await f.Initialize(expanded: true, voiceRecognition: capture);
            var main = f.Ui.Native.Main;
            main.BindSessionRoutingCaptureRelease(HandsFreeVoiceContinuation.CaptureRelease.Preserved);
            main.BindVoiceOwnershipGate(static () => true);
            await main.SetVoiceConsentAsync(true);
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => opening.TrySetResult(window));
            main.CommandText = "Explain held original";
            var operation = main.RunTypedCommand.ExecuteAsync();
            var window = await AwaitVoiceOpening(operation, opening.Task);
            capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(window, "RoutingAnswerByVoice");
            capture.Opens.Should().Be(1);
            Task? shutdown = null;
            if (reason is "cancel" or "close") { Click(window, "RoutingCancel"); }
            if (reason is "invalid") { capture.Transcript("shutdown"); }
            if (reason is "source")
            {
                await f.Ui.Native.Sessions.RenameAsync(f.Ui.First.Authority.SessionId, f.Ui.First.Authority.Generation,
                    f.Ui.First.Metadata!.Revision.Value, new("Renamed"), RequestOrigin.LocalUi, f.Ui.Native.Token);
            }
            if (reason is "shutdown") { shutdown = f.Routing.PrepareShutdown(); }
            await capture.StopStarted.Task;
            operation.IsCompleted.Should().BeFalse();
            if (shutdown is not null) { shutdown.IsCompleted.Should().BeFalse(); }
            capture.Transcript("1");
            capture.Closing.SetResult();
            await operation;
            if (shutdown is not null) { await shutdown; }
            f.Adapter.Calls.Should().Be(1);
            capture.IsCaptureQuiescent.Should().BeTrue();
            foreach (var entry in new[] { f.Ui.First, f.Ui.Second })
            {
                (await f.Ui.Native.Interactions.ReadConversationPageAsync(entry.Authority.SessionId,
                    null, 50, f.Ui.Native.Token)).Messages.Should().BeEmpty();
            }
        });
    }

    private static async Task<SessionRoutingWindow> AwaitVoiceOpening(Task operation, Task<SessionRoutingWindow> opening)
    {
        await Task.WhenAny(operation, opening);
        if (operation.IsCompleted) { await operation; throw new InvalidOperationException("Original operation returned before destination review."); }
        return await opening;
    }

    private static async Task SpeakChoice(SessionRoutingWindow window, RoutingReplyCapture capture, string text)
    {
        Click(window, "RoutingAnswerByVoice");
        capture.IsListening.Should().BeTrue(Find<TextBlock>(window, "RoutingVoiceStatus").Text);
        capture.Transcript(text);
        await window.VoiceOperation;
    }

    private sealed class RoutingReplyCapture : IQuestionVoiceRecognitionService, ISpeechInputProfileRuntime
    {
        private static readonly MicrophoneDevice Microphone = new("fixture-synthetic-microphone", "Synthetic fixture microphone");
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed { add { } remove { } }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged { add { } remove { } }
        public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted { add { } remove { } }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? ReplySpeechStarted { add { } remove { } }
        public long Generation { get; private set; }
        public long CaptureGeneration => Generation;
        public bool IsListening { get; private set; }
        public bool IsAmbientListeningAvailable => false;
        public bool IsCaptureQuiescent { get; private set; } = true;
        public SpeechInputGeneration ProfileGeneration { get; private set; } = new(1, SpeechInputSelection.Default);
        public bool IsProfileAvailable { get; private set; } = true;
        public void InitializeProfile(SpeechInputSelection? selection) => IsProfileAvailable = selection is not null;
        public Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken token)
        {
            IsProfileAvailable = false;
            return Task.FromResult<ISpeechInputProfileSwitch>(new ProfileSwitch(this));
        }
        private sealed class ProfileSwitch(RoutingReplyCapture owner) : ISpeechInputProfileSwitch
        {
            public void Publish(SpeechInputGeneration generation) { owner.ProfileGeneration = generation; owner.IsProfileAvailable = true; }
            public void HoldUnavailable() => owner.IsProfileAvailable = false;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        internal int Opens { get; private set; }
        internal TaskCompletionSource? Closing { get; set; }
        internal TaskCompletionSource StopStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<MicrophoneDevice> GetMicrophones() => [Microphone];
        public MicrophoneDevice? GetDefaultMicrophone() => Microphone;
        public bool AcceptCaptureGeneration(long generation) => generation == Generation;
        public void InvalidateCapture() { Generation++; IsListening = false; }
        public async Task StopAsync(CancellationToken token = default)
        {
            IsListening = false;
            StopStarted.TrySetResult();
            if (Closing is not null) { await Closing.Task.WaitAsync(token); }
            IsCaptureQuiescent = true;
        }
        public Task BeginReplyAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, Func<bool> admitted, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!IsCaptureQuiescent || !admitted()) { throw new InvalidOperationException("Synthetic reply admission denied."); }
            Generation++;
            Opens++;
            IsListening = true;
            IsCaptureQuiescent = false;
            return Task.CompletedTask;
        }
        internal void Transcript(string text) => TranscriptRecognized?.Invoke(this, new(text, 1, Generation));
        public Task EndCaptureAsync(CancellationToken token = default) => StopAsync(token);
        public Task EndPushToTalkAsync(CancellationToken token = default) => StopAsync(token);
        public Task StartAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not a global capture route.");
        public Task BeginPushToTalkAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not a PTT route.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
