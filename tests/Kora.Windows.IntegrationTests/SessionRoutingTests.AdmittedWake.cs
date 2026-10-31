using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.ViewModels;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Windows.Audio;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionRoutingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SpeechProfileStatusReadProgressesDuringNativeAdmissionAndRetirementJoinsPcm(
        bool dispose, bool retireDuringObservation)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new WakeNativeComposition();
            await f.Initialize(expanded: true);
            var native = f.Composition.Ui.Native;
            var session = await f.Stage.NextAsync();
            var statusEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var observationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var releaseStatus = new ManualResetEventSlim();
            using var releaseObservation = new ManualResetEventSlim();
            var pcmContext = new AsyncLocal<bool>();
            var statusArmed = 1;
            var observationArmed = 1;
            var allowObservation = false;
            f.StatusRuntime.BeforeAvailabilityRead = () =>
            {
                if (Interlocked.Exchange(ref statusArmed, 0) != 1) { return; }
                statusEntered.TrySetResult();
                releaseStatus.Wait(TestContext.Current.CancellationToken);
            };
            native.Access.BeforePrivacyObservation = () =>
            {
                if (!pcmContext.Value || Interlocked.Exchange(ref observationArmed, 0) != 1) { return; }
                observationEntered.TrySetResult();
                releaseObservation.Wait(TestContext.Current.CancellationToken);
                if (!Volatile.Read(ref allowObservation))
                { throw new OperationCanceledException("Synthetic admission barrier was withdrawn."); }
            };
            Task<string>? statusRead = null;
            Task? pcm = null;
            Task? retirement = null;
            try
            {
                statusRead = Task.Run(() => native.Main.SpeechInputStatus, TestContext.Current.CancellationToken);
                await statusEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                pcm = Task.Run(() => { pcmContext.Value = true; session.Wake(); }, TestContext.Current.CancellationToken);
                await observationEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                releaseStatus.Set();
                (await statusRead.WaitAsync(TestContext.Current.CancellationToken)).Should().Contain("Effective:");
                // Status holds the actual configuration lock and probes the voice runtime while PCM is observing.
                if (retireDuringObservation)
                {
                    session.HoldClose = true;
                    retirement = dispose ? f.Voice.DisposeAsync().AsTask()
                        : f.Voice.StopAsync(TestContext.Current.CancellationToken);
                    await session.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
                    retirement.IsCompleted.Should().BeFalse();
                    f.Voice.IsCaptureQuiescent.Should().BeFalse();
                    session.ReleaseClose.TrySetResult();
                    await session.Microphone.DisposalStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
                    retirement.IsCompleted.Should().BeFalse("native processing still owns the paused PCM callback");
                    session.Session.IsQuiescent.Should().BeFalse();
                    f.Voice.IsCaptureQuiescent.Should().BeFalse();
                }
                Volatile.Write(ref allowObservation, true);
                releaseObservation.Set();
                await pcm.WaitAsync(TestContext.Current.CancellationToken);
                if (!retireDuringObservation)
                {
                    await session.Capture.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
                    session.Detector.Samples.Should().Be(640);
                    session.OpenCount.Should().Be(1);
                    f.Voice.State.Should().Be(HandsFreeVoiceState.CapturingCommand);
                    f.Configuration.Get().Effective.Should().Be(f.StatusRuntime.ProfileGeneration);
                    session.HoldClose = true;
                    retirement = dispose ? f.Voice.DisposeAsync().AsTask()
                        : f.Voice.StopAsync(TestContext.Current.CancellationToken);
                    await session.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
                    retirement.IsCompleted.Should().BeFalse();
                    f.Voice.IsCaptureQuiescent.Should().BeFalse();
                }
                var samples = session.Detector.Samples;
                session.Microphone.Emit(new short[160]);
                session.Capture.Transcript("late command");
                session.Detector.Samples.Should().Be(samples);
                session.OpenCount.Should().Be(retireDuringObservation ? 0 : 1);
                f.Composition.Adapter.Calls.Should().Be(0);
                session.ReleaseClose.TrySetResult();
                if (retirement is null) { throw new InvalidOperationException("Synthetic capture retirement did not start."); }
                await retirement;
                await f.Voice.Completion;
                session.Session.IsQuiescent.Should().BeTrue();
                session.Assets.Disposed.Should().BeTrue();
                f.Voice.IsCaptureQuiescent.Should().BeTrue();
                f.Stage.Opens.Should().Be(1);
                f.Composition.Adapter.Calls.Should().Be(0);
                if (dispose) { f.Configuration.Get().Effective.Should().BeNull(); }
            }
            finally
            {
                releaseStatus.Set();
                releaseObservation.Set();
                f.Stage.ReleaseAll();
                // A failed assertion withdraws the fake observation before it can wait for configuration.
                try
                {
                    await Task.WhenAll(pcm ?? Task.CompletedTask,
                        statusRead is null ? Task.CompletedTask : statusRead, retirement ?? Task.CompletedTask);
                }
                finally
                {
                    native.Access.BeforePrivacyObservation = null;
                    f.StatusRuntime.BeforeAvailabilityRead = null;
                }
            }
        });
    }

    [Theory]
    [InlineData(false, "compare these plans")]
    [InlineData(true, "compare these plans")]
    [InlineData(false, "Nova explains these plans")]
    [InlineData(true, "Nova explains these plans")]
    public async Task IdleTypingThenNativeWakeSurvivesOwnedAdviceAndDestinationFocusTransfers(bool expanded, string body)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new WakeNativeComposition();
            await f.Initialize(expanded);
            var native = f.Composition.Ui.Native;
            var original = f.Composition.Ui.Window!;
            var session = await f.Stage.NextAsync();
            f.Composition.Ui.Model.ComposerBody = "independent session input";
            native.Main.CommandText = "independent main input";
            session.Microphone.Emit(new short[160]);
            f.Voice.Revalidate();
            f.Voice.State.Should().Be(HandsFreeVoiceState.WakeListening);
            native.Main.ListeningStatus.Should().StartWith("Wake listening");
            session.Detector.Samples.Should().Be(160);
            session.OpenCount.Should().Be(0);
            session.Session.IsQuiescent.Should().BeFalse();
            session.HoldClose = true;
            var adviceOpening = new TaskCompletionSource<SessionRoutingAdviceWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            var routeOpening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var adviceOpened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingAdviceWindow>((window, _) => adviceOpening.TrySetResult(window));
            using var routeOpened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => routeOpening.TrySetResult(window));
            session.Wake();
            await session.Capture.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            f.Voice.State.Should().Be(HandsFreeVoiceState.CapturingCommand);
            session.Capture.Transcript(body);
            session.Capture.Done.SetResult();
            await session.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
            f.Composition.Adapter.Calls.Should().Be(0);
            adviceOpening.Task.IsCompleted.Should().BeFalse();
            f.Voice.IsCaptureQuiescent.Should().BeFalse();
            session.ReleaseClose.SetResult();
            var advice = await AwaitWakeOpening(f.Voice, adviceOpening.Task);
            await ActivateRoutingWindow(advice, original);
            Dispatcher.UIThread.RunJobs();
            original.IsActive.Should().BeFalse("the native advice window actually takes foreground focus");
            advice.IsActive.Should().BeTrue();
            advice.IsVisible.Should().BeTrue();
            advice.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Name is "RoutingAdviceBody")
                .Text.Should().Be(body);
            session.Session.IsQuiescent.Should().BeTrue();
            session.Assets.Disposed.Should().BeTrue();
            f.Voice.IsCaptureQuiescent.Should().BeTrue();
            f.Stage.Opens.Should().Be(1);
            f.Composition.Adapter.Calls.Should().Be(0);

            ClickAdvice(advice, "RoutingAdviceAllow");
            var route = await AwaitWakeOpening(f.Voice, routeOpening.Task,
                () => native.Main.ResponseTitle + " | " + native.Main.ResponseBody + " | " + f.Diagnostics.Failure);
            await ActivateRoutingWindow(route, original);
            Dispatcher.UIThread.RunJobs();
            route.IsActive.Should().BeTrue();
            original.IsActive.Should().BeFalse();
            advice.Content.Should().BeNull();
            f.Composition.Adapter.Calls.Should().Be(1);
            await Choose(route);
            var answering = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseAnswer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Composition.Adapter.HoldAnswer = async () =>
            {
                answering.TrySetResult();
                await releaseAnswer.Task.WaitAsync(TestContext.Current.CancellationToken);
            };
            try
            {
                Click(route, "RoutingSubmit");
                await answering.Task.WaitAsync(TestContext.Current.CancellationToken);
                f.Stage.Opens.Should().Be(1, "no ambient capture may rearm during an admitted model operation");
                f.Composition.Adapter.Requests.Should().OnlyContain(request => request.Origin == RequestOrigin.ActivatedVoice);
                f.Composition.Adapter.Requests[1].SessionId.Should().Be(f.Composition.Ui.First.Authority.SessionId);
            }
            finally { releaseAnswer.TrySetResult(); }
            // Stop an optional fresh wake generation without revoking the request while it is still running.
            var terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Presented(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (args.PropertyName is nameof(native.Main.ResponseBody)
                    && native.Main.ResponseBody.Contains("Genuine second-turn answer", StringComparison.Ordinal))
                { terminal.TrySetResult(); }
            }
            native.Main.PropertyChanged += Presented;
            try
            {
                if (!native.Main.ResponseBody.Contains("Genuine second-turn answer", StringComparison.Ordinal))
                { await terminal.Task.WaitAsync(TestContext.Current.CancellationToken); }
            }
            finally { native.Main.PropertyChanged -= Presented; }
            var page = await native.Interactions.ReadConversationPageAsync(f.Composition.Ui.First.Authority.SessionId,
                null, 50, native.Token);
            page.Messages.Should().ContainSingle().Which.Body.Should().Be(body);
            page.Messages[0].AssistantResponse!.Body.Should().Be("Genuine second-turn answer");
            f.Composition.Adapter.Originals.Should().Equal(body, body);
            native.Voice.StartCalls.Should().Be(0);
            native.Main.CommandText.Should().Be("independent main input");
            f.Composition.Ui.Model.ComposerBody.Should().Be("independent session input");
            f.Composition.Audit.Events.Where(item => string.Equals(item.ActionId, "model.turn", StringComparison.Ordinal))
                .Count(item => item.Outcome == SecurityAuditOutcome.Succeeded).Should().Be(4);
            f.Composition.Turns.IsQuiescent.Should().BeTrue();
            f.Voice.State.Should().NotBe(HandsFreeVoiceState.RecoveryRequired);
        });
    }

    [Theory]
    [InlineData("activation-gap")]
    [InlineData("capture")]
    public async Task ComposerEditAfterActualNativeActivationCannotOpenRoutingOrRetainARequest(string phase)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new WakeNativeComposition();
            await f.Initialize(expanded: true);
            var native = f.Composition.Ui.Native;
            var session = await f.Stage.NextAsync();
            if (phase is "activation-gap")
            { session.AfterActivation = () => native.Main.CommandText = "replacement after native name detection"; }
            session.Wake();
            if (phase is "capture")
            {
                await session.Capture.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
                native.Main.CommandText = "replacement during command capture";
                session.Capture.Transcript("compare these plans");
                session.Microphone.Emit(new short[160]);
                session.Capture.Done.TrySetResult();
            }
            await f.Voice.Completion;
            f.Voice.State.Should().Be(HandsFreeVoiceState.RecoveryRequired);
            f.Voice.IsCaptureQuiescent.Should().BeTrue();
            session.Session.IsQuiescent.Should().BeTrue();
            session.Assets.Disposed.Should().BeTrue();
            if (phase is "activation-gap") { session.OpenCount.Should().Be(0); }
            f.Stage.Opens.Should().Be(1);
            f.Composition.Adapter.Calls.Should().Be(0);
            (await native.Interactions.ReadConversationPageAsync(f.Composition.Ui.First.Authority.SessionId,
                null, 50, native.Token)).Messages.Should().BeEmpty();
            f.Composition.Audit.Events.Should().NotContain(item => item.ActionId == "model.turn");
        });
    }

    [Theory]
    [InlineData("composer")]
    [InlineData("session")]
    [InlineData("replacement")]
    [InlineData("hide")]
    [InlineData("unrelated-focus")]
    [InlineData("reviewed-command-focus")]
    public async Task NativeAdviceFocusLeaseDoesNotRenewChangedSourceOrAcceptLateConsent(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new WakeNativeComposition();
            await f.Initialize(expanded: true);
            var native = f.Composition.Ui.Native;
            var session = await f.Stage.NextAsync();
            var opening = new TaskCompletionSource<SessionRoutingAdviceWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingAdviceWindow>((window, _) => opening.TrySetResult(window));
            session.Wake();
            await session.Capture.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            session.Capture.Transcript("compare these plans");
            session.Capture.Done.SetResult();
            var advice = await AwaitWakeOpening(f.Voice, opening.Task);
            await ActivateRoutingWindow(advice, f.Composition.Ui.Window);
            Dispatcher.UIThread.RunJobs();
            advice.IsVisible.Should().BeTrue();
            f.Composition.Ui.Window!.IsActive.Should().BeFalse();
            var late = advice.GetVisualDescendants().OfType<Button>().Single(item => item.Name is "RoutingAdviceAllow");
            var other = change is "reviewed-command-focus" ? new ResponseWindow(native.Main) : new Window();
            try
            {
                if (change is "composer") { native.Main.CommandText = "replacement input"; }
                if (change is "session") { await f.Composition.Ui.Model.SelectAsync(f.Composition.Ui.Second); }
                if (change is "replacement")
                { native.Main.PublishForegroundConversationTarget(new(f.Composition.Ui.Second, static () => true)); }
                if (change is "hide") { f.Composition.Ui.Window.Hide(); }
                if (change is "unrelated-focus" or "reviewed-command-focus")
                { other.Show(); await ActivateRoutingWindow(other, advice); }
                Dispatcher.UIThread.RunJobs();
                late.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await f.Voice.Completion;
                f.Composition.Adapter.Calls.Should().Be(0);
                f.Stage.Opens.Should().Be(1);
                advice.Content.Should().BeNull();
                f.Voice.IsCaptureQuiescent.Should().BeTrue();
                native.Main.HasReviewedVoiceCommand.Should().BeFalse();
                native.Main.ReviewedVoiceCommandText.Should().BeEmpty();
                native.Voice.StartCalls.Should().Be(0);
                f.Composition.Audit.Events.Should().NotContain(item => item.ActionId == "model.turn");
                foreach (var entry in new[] { f.Composition.Ui.First, f.Composition.Ui.Second })
                {
                    (await native.Interactions.ReadConversationPageAsync(entry.Authority.SessionId,
                        null, 50, native.Token)).Messages.Should().BeEmpty();
                }
            }
            finally { other.Close(); }
        });
    }

    private static async Task<T> AwaitWakeOpening<T>(HandsFreeVoiceRecognitionService voice, Task<T> opening,
        Func<string>? failure = null)
    {
        await Task.WhenAny(voice.Completion, opening);
        if (!opening.IsCompleted)
        {
            await voice.Completion;
            throw new InvalidOperationException("Native wake returned before its exact routing window opened. "
                + voice.State + ": " + failure?.Invoke());
        }
        return await opening;
    }

    private static async Task ActivateRoutingWindow(Window window, Window? previous = null)
    {
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Window.IsActiveProperty && window.IsActive) { active.TrySetResult(); }
        }
        window.PropertyChanged += Changed;
        try
        {
            window.Activate();
            if (!window.IsActive) { await active.Task.WaitAsync(TestContext.Current.CancellationToken); }
            // Headless activation does not emulate the OS deactivation of the previous foreground window.
            if (previous is { IsActive: true }) { DeactivateHeadlessWindow(previous); }
        }
        finally { window.PropertyChanged -= Changed; }
    }

    // Avalonia's headless platform has no public deactivation injector; run its real notification handler.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "HandleDeactivated")]
    private static extern void DeactivateHeadlessWindow(WindowBase window);

    private sealed class WakeNativeComposition : IAsyncDisposable
    {
        internal LocalComposition Composition { get; } = new(syntheticVoice: true);
        internal NativeWakeStage Stage { get; } = new();
        internal HandsFreeVoiceRecognitionService Voice { get; }
        internal NativeWakeStatusRuntime StatusRuntime { get; }
        internal WakeDiagnosticLogger Diagnostics { get; } = new();
        private readonly AudioControlAdmission control;
        private readonly SpeechInputConfigurationService configuration;
        internal SpeechInputConfigurationService Configuration => configuration;

        internal WakeNativeComposition()
        {
            Voice = new(new NativeWakeAdapter(), Stage, NullLogger<HandsFreeVoiceRecognitionService>.Instance);
            StatusRuntime = new(Voice);
            var native = Composition.Ui.Native;
            native.MainLogger = Diagnostics;
            control = new(native.Interactions, native.Interactions, new(native.Tasks));
            configuration = new(new NativeWakePreferences(), StatusRuntime, control, native.Audit,
                NullLogger<SpeechInputConfigurationService>.Instance);
        }

        internal sealed class NativeWakeStatusRuntime(ISpeechInputProfileRuntime runtime) : ISpeechInputProfileRuntime
        {
            internal Action? BeforeAvailabilityRead { get; set; }
            public SpeechInputGeneration ProfileGeneration => runtime.ProfileGeneration;
            public bool IsProfileAvailable
            {
                get
                {
                    BeforeAvailabilityRead?.Invoke();
                    return runtime.IsProfileAvailable;
                }

            }
            public void InitializeProfile(SpeechInputSelection? selection) => runtime.InitializeProfile(selection);
            public Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken token) => runtime.RetireProfileAsync(token);
        }

        internal sealed class WakeDiagnosticLogger : ILogger<MainViewModel>
        {
            internal Exception? Failure { get; private set; }
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (exception is not null) { Failure = exception; }
            }
        }

        internal async Task Initialize(bool expanded)
        {
            Composition.Ui.AutoAllowAdvice = false;
            await Composition.Initialize(expanded, voiceRecognition: Voice, speechInputConfiguration: configuration);
            var main = Composition.Ui.Native.Main;
            main.BindReviewedVoiceCommand(NullLogger<ReviewedVoiceCommandCapture>.Instance, TimeProvider.System);
            await main.SetAssistantNameAsync("Nova");
            Composition.Ui.Window!.ShowSessions(expanded);
            await ActivateRoutingWindow(Composition.Ui.Window);
            main.BindVoiceOwnershipGate(static () => true);
            await main.SetVoiceConsentAsync(true);
            main.IsVoiceEnabled.Should().BeTrue();
        }

        public async ValueTask DisposeAsync()
        {
            Voice.InvalidateCapture();
            Stage.ReleaseAll();
            if (Composition.Routing is not null) { await Composition.Routing.PrepareShutdown(); }
            await Voice.StopAsync(TestContext.Current.CancellationToken);
            await Voice.Completion;
            await configuration.DisposeAsync();
            await control.DisposeAsync();
            await Voice.DisposeAsync();
            await Composition.DisposeAsync();
        }
    }

    private sealed class NativeWakePreferences : ISpeechInputPreferences
    {
        public SpeechInputSelection? Load() => null;
        public SpeechInputSelection? ReadBack() => null;
        public void BeginWrite() =>
            throw new InvalidOperationException("Wake regressions do not begin preference writes.");
        public void Save(SpeechInputSelection selection) =>
            throw new InvalidOperationException("Wake regressions do not change saved speech-input selections.");
        public void ConfirmWrite() =>
            throw new InvalidOperationException("Wake regressions do not confirm preference writes.");
    }

    private sealed class NativeWakeStage : IHandsFreeAudioStage
    {
        private readonly Channel<NativeWakeSession> openings = Channel.CreateUnbounded<NativeWakeSession>();
        private readonly ConcurrentBag<NativeWakeSession> sessions = [];
        public bool IsAvailable => true;
        internal int Opens { get; private set; }
        public Task<IHandsFreeAudioSession> OpenAsync(HandsFreeAudioBinding binding, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Opens++;
            var session = new NativeWakeSession(binding);
            sessions.Add(session);
            session.Session.Start();
            openings.Writer.TryWrite(session).Should().BeTrue();
            return Task.FromResult<IHandsFreeAudioSession>(session);
        }
        internal async Task<NativeWakeSession> NextAsync() => await openings.Reader.ReadAsync(TestContext.Current.CancellationToken);
        internal void ReleaseAll()
        {
            foreach (var session in sessions)
            {
                session.ReleaseClose.TrySetResult();
                session.Capture.Done.TrySetResult();
            }
        }
    }

    private sealed class NativeWakeSession : IHandsFreeAudioSession
    {
        internal NativeWakeMicrophone Microphone { get; } = new();
        internal NativeWakeDetector Detector { get; } = new();
        internal NativeWakeCapture Capture { get; } = new();
        internal NativeWakeAssets Assets { get; } = new();
        internal WindowsHandsFreeAudioSession Session { get; }
        internal TaskCompletionSource Closing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseClose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool HoldClose { get; set; }
        internal Action? AfterActivation { get; set; }
        internal int OpenCount { get; private set; }
        internal NativeWakeSession(HandsFreeAudioBinding binding)
        {
            binding.AssistantName.Should().Be("Nova");
            Session = new(binding with
            {
                WakeActivated = wake => { binding.WakeActivated(wake); AfterActivation?.Invoke(); },
            }, Microphone, Detector, new NativeWakeEndpoint(), Assets, _ =>
            {
                OpenCount++;
                return Task.FromResult<IActivatedCapture>(Capture);
            });
        }
        internal void Wake()
        {
            Detector.Next = new(1, 1);
            Microphone.Emit(new short[640]);
        }
        public bool IsQuiescent => Session.IsQuiescent;
        public Task<WakeWordObservation> WaitForWakeAsync(CancellationToken token) => Session.WaitForWakeAsync(token);
        public Task<VoiceTranscriptEventArgs?> CaptureCommandAsync(WakeWordObservation wake, CancellationToken token) =>
            Session.CaptureCommandAsync(wake, token);
        public void Invalidate() => Session.Invalidate();
        public async ValueTask DisposeAsync()
        {
            Closing.TrySetResult();
            if (HoldClose) { await ReleaseClose.Task.WaitAsync(TestContext.Current.CancellationToken); }
            await Session.DisposeAsync();
        }
    }

    private sealed class NativeWakeMicrophone : IHandsFreeMicrophone
    {
        public event HandsFreeSamplesAvailable? Samples;
        public event EventHandler? Failed { add { } remove { } }
        public void Start() { }
        internal TaskCompletionSource DisposalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Emit(short[] samples) => Samples?.Invoke(samples);
        public ValueTask DisposeAsync() { DisposalStarted.TrySetResult(); return ValueTask.CompletedTask; }
    }

    private sealed class NativeWakeDetector : IHandsFreeKeywordDetector
    {
        internal KeywordAudioSegment? Next { get; set; }
        internal int Samples { get; private set; }
        public KeywordAudioSegment? Accept(ReadOnlySpan<short> samples)
        {
            Samples += samples.Length;
            return Next;
        }
        public void Dispose() { }
    }

    private sealed class NativeWakeEndpoint : IHandsFreeCommandEndpoint
    {
        public bool Accept(ReadOnlySpan<short> samples) => false;
        public void Dispose() { }
    }

    private sealed class NativeWakeAssets : IAsyncDisposable
    {
        internal bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class NativeWakeCapture : IActivatedCapture
    {
        public event ActivatedAudioAvailable? DataAvailable { add { } remove { } }
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed { add { } remove { } }
        public event EventHandler? SpeechDetected { add { } remove { } }
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion => Done.Task;
        public long? RecorderReleasedTimestamp => null;
        public void Start() => Started.TrySetResult();
        internal void Transcript(string text) => TranscriptRecognized?.Invoke(this, new(text, 1));
        public Task ReleaseRecorder() => Task.CompletedTask;
        public void FinishRecognition(bool cancel) { if (cancel) { Done.TrySetResult(); } }
        public ValueTask DisposeAsync() { Done.TrySetResult(); return ValueTask.CompletedTask; }
    }

    private sealed class NativeWakeAdapter : IQuestionVoiceRecognitionService, ISpeechInputProfileRuntime
    {
        private static readonly MicrophoneDevice Microphone = new("fixture-synthetic-microphone", "Synthetic fixture microphone");
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized { add { } remove { } }
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed { add { } remove { } }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged { add { } remove { } }
        public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted { add { } remove { } }
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? ReplySpeechStarted { add { } remove { } }
        public long Generation => 0;
        public long CaptureGeneration => 0;
        public bool IsListening => false;
        public bool IsAmbientListeningAvailable => false;
        public bool IsCaptureQuiescent => true;
        public bool IsLiteralDictationAvailable => false;
        public SpeechInputGeneration ProfileGeneration { get; private set; } = new(1, SpeechInputSelection.Default);
        public bool IsProfileAvailable { get; private set; }
        public void InitializeProfile(SpeechInputSelection? selection) => IsProfileAvailable = selection is not null;
        public Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken token)
        {
            IsProfileAvailable = false;
            return Task.FromResult<ISpeechInputProfileSwitch>(new NativeWakeProfileSwitch(this));
        }
        public IReadOnlyList<MicrophoneDevice> GetMicrophones() => [Microphone];
        public MicrophoneDevice? GetDefaultMicrophone() => Microphone;
        public bool AcceptCaptureGeneration(long generation) => false;
        public void InvalidateCapture() { }
        public Task StopAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task EndCaptureAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task EndPushToTalkAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task StartAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No ambient transcription in wake regressions.");
        public Task BeginPushToTalkAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No PTT substitute in wake regressions.");
        public Task BeginReplyAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, Func<bool> admitted,
            CancellationToken token) => throw new InvalidOperationException("No reply capture substitute in wake regressions.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class NativeWakeProfileSwitch(NativeWakeAdapter owner) : ISpeechInputProfileSwitch
        {
            public void Publish(SpeechInputGeneration generation) { owner.ProfileGeneration = generation; owner.IsProfileAvailable = true; }
            public void HoldUnavailable() => owner.IsProfileAvailable = false;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
