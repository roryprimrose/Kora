using Kora.Application.Interaction;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Interaction;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Interaction;

internal sealed class QuestionReplyFixture : IAsyncDisposable
{
    public InteractionFixture Host { get; } = new();
    public ReplyTime Time { get; } = new();
    public ReplyCapture Capture { get; } = new();
    public ReplyDispatcher Dispatcher { get; } = new();
    public HostQuestionVoiceReplyService Replies { get; }
    private readonly IQuestionVoiceRecognitionService captureRuntime;
    public QuestionVoiceAdmission? Admission { get; set; } = new(new("mic", "Synthetic"), 1, 1, 1);
    public bool Visible { get; set; } = true;
    public ConversationalReplyState? Preference { get; set; }
    public bool ThrowPlayback { get; set; }
    public TaskCompletionSource? Playback { get; set; }
    public bool Speaking { get; private set; }
    public bool PlaybackEligible { get; private set; }
    public HostInteractionDecision? Accepted { get; private set; }
    public int SpeechCalls { get; private set; }
    public Action? BeforeObserve { get; set; }
    public QuestionReplyFixture(Func<ReplyCapture, IQuestionVoiceRecognitionService>? captureDecorator = null)
    {
        Time.Now = Host.Time.Now;
        captureRuntime = captureDecorator?.Invoke(Capture) ?? Capture;
        Replies = new(Host.Questions, captureRuntime, Time, Dispatcher, () => { BeforeObserve?.Invoke(); return Admission; },
            async (_, eligible, token) =>
            {
                SpeechCalls++;
                Speaking = true;
                try
                {
                    PlaybackEligible = eligible();
                    if (ThrowPlayback) { throw new IOException("Synthetic playback failure"); }
                    if (Playback is not null) { await Playback.Task.WaitAsync(token); }
                }
                finally { Speaking = false; }
            }, NullLogger<HostQuestionVoiceReplyService>.Instance, conversationalPreference: () => Preference);
    }

    public Task OpenAsync(HostQuestionRecord question, bool afterPlayback = false) =>
        Replies.OpenAsync(question, () => Visible, decision => Accepted = decision, afterPlayback, TestContext.Current.CancellationToken);

    public Task OpenConversationalAsync(HostQuestionRecord question) =>
        Replies.OpenConversationalAsync(question, () => Visible, decision => Accepted = decision, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Replies.DisposeAsync();
        await captureRuntime.DisposeAsync();
        Host.Dispose();
    }

    internal sealed class ReplyDispatcher : IUiDispatcher
    {
        public TaskCompletionSource? Hold { get; set; }
        public async Task InvokeAsync(Func<Task> action)
        {
            if (Hold is not null) { await Hold.Task; }
            await action();
        }
        public void Post(Action action) => action();
    }

    internal sealed class ReplyCapture : IQuestionVoiceRecognitionService
    {
        public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;
        public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged;
        public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted;
        public event EventHandler<VoiceCaptureStateChangedEventArgs>? ReplySpeechStarted;
        public bool IsListening { get; private set; }
        public long Generation { get; private set; }
        public long CaptureGeneration
        {
            get
            {
                var generation = Generation;
                if (Stops > 0) { StoppedGenerationObserved?.TrySetResult(); }
                return generation;
            }
        }
        public TaskCompletionSource? StoppedGenerationObserved { get; set; }
        public bool PreserveGenerationOnInvalidate { get; set; }
        public bool IsAmbientListeningAvailable => false;
        public bool IsCaptureQuiescent => !IsListening && !NonQuiescent;
        public bool NonQuiescent { get; set; }
        public bool RejectAcknowledgement { get; set; }
        public Exception? OpenFailure { get; set; }
        public Exception? StopFailure { get; set; }
        public Action? BeforeOpen { get; set; }
        public TaskCompletionSource? Opening { get; set; }
        public TaskCompletionSource? Closing { get; set; }
        public int Opens { get; private set; }
        public int Stops { get; private set; }
        public TaskCompletionSource StopStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<bool>? Admitted { get; private set; }
        public CancellationToken CaptureToken { get; private set; }
        public async Task BeginReplyAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
            Func<bool> isAdmitted, CancellationToken cancellationToken)
        {
            Admitted = isAdmitted;
            CaptureToken = cancellationToken;
            BeforeOpen?.Invoke();
            if (OpenFailure is not null) { throw OpenFailure; }
            if (Opening is not null) { await Opening.Task.WaitAsync(cancellationToken); }
            cancellationToken.ThrowIfCancellationRequested();
            if (!isAdmitted()) { throw new InvalidOperationException("Admission withdrawn"); }
            Generation++;
            Opens++;
            IsListening = true;
        }
        public bool AcceptCaptureGeneration(long generation) => !RejectAcknowledgement && generation == Generation;
        public void InvalidateCapture()
        {
            if (!PreserveGenerationOnInvalidate) { Generation++; }
            IsListening = false;
            CaptureStateChanged?.Invoke(this, new(Generation, false));
        }
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            Stops++;
            StopStarted.TrySetResult();
            if (StopFailure is not null) { throw StopFailure; }
            IsListening = false;
            if (Closing is not null) { await Closing.Task.WaitAsync(cancellationToken); }
        }
        public void Transcript(string text, long? generation = null) => TranscriptRecognized?.Invoke(this, new(text, 1, generation ?? Generation));
        public void Speech(long? generation = null) => ReplySpeechStarted?.Invoke(this, new(generation ?? Generation, true));
        public void Fail(long? generation = null) => RecognitionFailed?.Invoke(this, new("Synthetic", generation ?? Generation));
        public void AdvanceGeneration() => Generation++;
        public void Complete(long? generation = null) => RecognitionCompleted?.Invoke(this,
            new(generation ?? Generation, VoiceRecognitionCompletionReason.NoSpeechRecognized));
        public Task BeginPushToTalkAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not a PTT route");
        public Task StartAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, string? assistantName = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Not a command route");
        public Task EndPushToTalkAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("No button release");
        public Task EndCaptureAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("No button release");
        public IReadOnlyList<MicrophoneDevice> GetMicrophones() => [];
        public MicrophoneDevice? GetDefaultMicrophone() => null;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class ReplyTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public List<ReplyTimer> Timers { get; } = [];
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ReplyTimer(() => callback(state), Now + dueTime, period);
            Timers.Add(timer);
            return timer;
        }
        public void Advance(TimeSpan duration)
        {
            Now += duration;
            foreach (var timer in Timers.ToArray())
            {
                if (!timer.Disposed && timer.Due <= Now)
                {
                    timer.Tick();
                    timer.Due = timer.Period == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : Now + timer.Period;
                }
            }
        }
        internal sealed class ReplyTimer(Action tick, DateTimeOffset due, TimeSpan period) : ITimer
        {
            public bool Disposed { get; private set; }
            public DateTimeOffset Due { get; set; } = due;
            public TimeSpan Period { get; } = period;
            public void Tick() => tick();
            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
