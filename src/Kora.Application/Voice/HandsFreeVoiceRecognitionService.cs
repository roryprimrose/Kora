using System.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

/// <summary>One host owner for wake, activated capture, governed dispatch and quiescent re-arm. Borrows the activated adapter.</summary>
public sealed partial class HandsFreeVoiceRecognitionService(
    IQuestionVoiceRecognitionService activated,
    IHandsFreeAudioStage audio,
    ILogger<HandsFreeVoiceRecognitionService> logger,
    TimeProvider? timeProvider = null) : IQuestionVoiceRecognitionService, ISpeechInputProfileRuntime
{
    private readonly Lock gate = new();
    private Func<HandsFreeVoiceAdmission?>? observe;
    private Func<VoiceTranscriptEventArgs, HostRequest, Task>? dispatch;
    private Func<bool> canOpen = static () => false;
    private Action<long> commandActivated = static _ => { };
    private Func<IReadOnlyList<string>> recognitionPhrases = static () => [];
    private CancellationTokenSource? cancellation;
    private IHandsFreeAudioSession? session;
    private HandsFreeVoiceAdmission? admission;
    private WakeWordObservation? admittedWake;
    private VoiceTranscriptEventArgs? pendingWakeUtterance;
    private bool wakeUtteranceConsumed;
    private Task run = Task.CompletedTask;
    private TaskCompletionSource captureClosed = CompletedReceipt();
    private long sequence;
    private long ambientGeneration;
    private bool disposed;
    private HandsFreeVoiceState state = audio.IsAvailable ? HandsFreeVoiceState.Stopped : HandsFreeVoiceState.Unavailable;

    public event EventHandler? Changed;
    public HandsFreeVoiceState State { get { lock (gate) { return state; } } }
    public bool IsAmbientListeningAvailable => audio.IsAvailable;
    public bool IsCaptureQuiescent
    {
        get
        {
            lock (gate) { return captureClosed.Task.IsCompletedSuccessfully && activated.IsCaptureQuiescent; }
        }
    }
    public long Generation
    {
        get
        {
            var value = Interlocked.Read(ref ambientGeneration);
            return value != 0 ? value : activated.Generation;
        }
    }
    public long CaptureGeneration => Generation;
    public bool IsListening => State is HandsFreeVoiceState.WakeListening or HandsFreeVoiceState.CapturingCommand || activated.IsListening;

    public void Bind(Func<HandsFreeVoiceAdmission?> observation,
        Func<VoiceTranscriptEventArgs, HostRequest, Task> governedDispatch, Func<bool> canOpenCapture,
        Action<long>? onCommandActivated = null, Func<IReadOnlyList<string>>? getRecognitionPhrases = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(governedDispatch);
        ArgumentNullException.ThrowIfNull(canOpenCapture);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (observe is not null) { throw new InvalidOperationException("The hands-free host is already bound."); }
            observe = observation;
            dispatch = governedDispatch;
            canOpen = canOpenCapture;
            commandActivated = onCommandActivated ?? (static _ => { });
            recognitionPhrases = getRecognitionPhrases ?? (static () => []);
        }
    }

    public async Task StartHandsFreeAsync(bool explicitRecovery = false, CancellationToken cancellationToken = default)
    {
        Task opened;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (profileSwitchActive || profileRetirementFailed) { throw new InvalidOperationException("Speech-input switching or unconfirmed retirement owns the capture/runtime lifecycle."); }
            if (!audio.IsAvailable) { return; }
            if (observe is null) { throw new InvalidOperationException("The hands-free host is not bound."); }
            if (!run.IsCompleted || !captureClosed.Task.IsCompletedSuccessfully || !activated.IsCaptureQuiescent)
            {
                throw new InvalidOperationException("Previous voice work has not completed and joined.");
            }
            if (state == HandsFreeVoiceState.RecoveryRequired && !explicitRecovery)
            {
                throw new InvalidOperationException("Explicit listening recovery is required.");
            }
            cancellation?.Dispose();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var firstOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            opened = firstOpen.Task;
            ActivityLink[] causes = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
            run = Task.Run(() => RunAsync(firstOpen, causes, cancellation.Token), CancellationToken.None);
        }
#pragma warning disable VSTHRD003 // Join the owned first-open receipt; never synchronously block native/UI delivery.
        await opened;
#pragma warning restore VSTHRD003
    }

    public void Revalidate()
    {
        var observation = ObserveAdmission();
        var invalidated = false;
        lock (gate)
        {
            if (admission is not null && !IsObservedAdmissionCurrent(observation))
            {
                InvalidateCaptureCore();
                invalidated = true;
            }
        }
        if (invalidated) { activated.InvalidateCapture(); }
    }

    public void InvalidateCapture()
    {
        lock (gate) { InvalidateCaptureCore(); }
        activated.InvalidateCapture();
    }

    private void InvalidateCaptureCore()
    {
        Interlocked.Exchange(ref ambientGeneration, -Interlocked.Increment(ref sequence) - 1);
        cancellation?.Cancel();
        session?.Invalidate();
        admission = null;
        admittedWake = null;
        pendingWakeUtterance = null;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task worker;
        bool joinWorker;
        lock (gate)
        {
            worker = run;
            joinWorker = state != HandsFreeVoiceState.CompletingOperation;
            InvalidateCaptureCore();
        }
        activated.InvalidateCapture();
        Task closure;
        lock (gate) { closure = captureClosed.Task; }
        // Dispatch can itself execute Disable listening. Join capture, not the operation awaiting dispatch.
#pragma warning disable VSTHRD003 // Owned capture release is independent of the governed operation and dispatcher.
        await Task.WhenAll(closure, activated.StopAsync(cancellationToken));
        if (joinWorker) { await worker; }
#pragma warning restore VSTHRD003
    }

    private async Task RunAsync(TaskCompletionSource firstOpen, ActivityLink[] causes, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var current = observe!() ?? throw new InvalidOperationException("Fresh hands-free policy admission is unavailable.");
                if (!canOpen()) { throw new InvalidOperationException("Another input or playback owner is active."); }
                var generation = -Interlocked.Increment(ref sequence) - 1;
                var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
                using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request, causes);
                try
                {
                    VoiceTranscriptEventArgs? transcript;
                    lock (gate)
                    {
                        token.ThrowIfCancellationRequested();
                        admission = current;
                        admittedWake = null;
                        Interlocked.Exchange(ref ambientGeneration, generation);
                        captureClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    try
                    {
                        var binding = new HandsFreeAudioBinding(generation, current.Microphone,
                            current.AssistantName, current.Profile)
                        {
                            CanCapture = () => IsCaptureAdmissionCurrent(generation, token),
                            CommandPhrases = Array.AsReadOnly(recognitionPhrases().ToArray()),
                            WakeActivated = wake => AdmitWake(wake, generation, token),
                        };
                        var opened = await audio.OpenAsync(binding, token);
                        lock (gate)
                        {
                            session = opened;
                            if (token.IsCancellationRequested) { opened.Invalidate(); }
                        }
                        RequireCurrent(generation, token);
                        HandsFreeVoiceState openedState;
                        lock (gate)
                        {
                            openedState = state = admittedWake is null ? HandsFreeVoiceState.WakeListening : HandsFreeVoiceState.CapturingCommand;
                        }
                        PublishStateChanged(openedState);
                        firstOpen.TrySetResult();
                        var wake = await opened.WaitForWakeAsync(token);
                        AdmitWake(wake, generation, token);
                        lock (gate) { current = admission!; }
                        using var maximumDuration = new CancellationTokenSource(VoiceInputLimits.MaximumCommandDuration,
                            timeProvider ?? TimeProvider.System);
                        using var commandDeadline = CancellationTokenSource.CreateLinkedTokenSource(token, maximumDuration.Token);
                        transcript = await opened.CaptureCommandAsync(wake, commandDeadline.Token);
                        RequireCurrent(generation, token);
                        if (transcript is not null && (transcript.Generation != generation
                            || string.IsNullOrWhiteSpace(transcript.Transcript)
                            || transcript.Transcript.Length > VoiceInputLimits.MaximumTranscriptCharacters
                            || transcript.ActivationEvidence is not null
                            || !float.IsFinite(transcript.Confidence) || transcript.Confidence is < 0 or > 1))
                        {
                            throw new InvalidOperationException("Final command was invalid or belonged to another capture.");
                        }
                    }
                    finally
                    {
                        await CloseCaptureAsync();
                    }
                    RequireCurrent(generation, token);
                    if (transcript is not null)
                    {
                        var observation = ObserveAdmission(requireCaptureOwner: true);
                        lock (gate)
                        {
                            token.ThrowIfCancellationRequested();
                            if (!IsObservedAdmissionCurrent(observation)
                                || ambientGeneration != generation)
                            {
                                throw new InvalidOperationException("Voice admission changed before dispatch.");
                            }
                            var evidence = new NativeWakeEvidence(current, generation, request);
                            transcript = new(transcript.Transcript, transcript.Confidence, generation, evidence);
                            pendingWakeUtterance = transcript;
                            wakeUtteranceConsumed = false;
                            state = HandsFreeVoiceState.CompletingOperation;
                        }
                        PublishStateChanged(HandsFreeVoiceState.CompletingOperation);
                        try { await dispatch!(transcript, request); }
                        finally { lock (gate) { pendingWakeUtterance = null; } }
                    }
                    activity.Complete(HostOperationOutcome.Completed);
                    causes = activity.Activity is { } completed ? [new(completed.Context)] : [];
                    // A command may intentionally change readiness/name/profile or stop listening.
                    if (token.IsCancellationRequested || observe!() != current || !canOpen()) { break; }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    activity.Complete(HostOperationOutcome.Cancelled);
                    throw;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    activity.Complete(HostOperationOutcome.Failed);
                    throw;
                }
            }
            SetState(HandsFreeVoiceState.Stopped);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            firstOpen.TrySetCanceled(token);
            SetState(HandsFreeVoiceState.Stopped);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            firstOpen.TrySetException(exception);
            LifecycleFailed(logger, exception.GetType().FullName);
            SetState(HandsFreeVoiceState.RecoveryRequired);
        }
        finally
        {
            firstOpen.TrySetCanceled(CancellationToken.None);
            lock (gate) { admission = null; admittedWake = null; pendingWakeUtterance = null; }
        }
    }

    internal Func<bool> ConsumeWakeActivation(VoiceTranscriptEventArgs utterance, HostRequest request)
    {
        var observation = ObserveAdmission(requireCaptureOwner: true);
        var activatedQuiescent = activated.IsCaptureQuiescent;
        lock (gate)
        {
            if (wakeUtteranceConsumed || !IsWakeActivationCurrentCore(utterance, request, observation, activatedQuiescent))
            {
                throw new InvalidOperationException("The original host-owned wake utterance is unavailable or already consumed.");
            }
            wakeUtteranceConsumed = true;
            return () => IsWakeActivationCurrent(utterance, request);
        }
    }

    private bool IsWakeActivationCurrent(VoiceTranscriptEventArgs utterance, HostRequest request)
    {
        var observation = ObserveAdmission();
        var activatedQuiescent = activated.IsCaptureQuiescent;
        lock (gate) { return IsWakeActivationCurrentCore(utterance, request, observation, activatedQuiescent); }
    }

    private bool IsWakeActivationCurrentCore(VoiceTranscriptEventArgs utterance, HostRequest request,
        AdmissionObservation? observation, bool activatedQuiescent)
    {
        if (state != HandsFreeVoiceState.CompletingOperation
            || request.Origin != RequestOrigin.ActivatedVoice
            || utterance.ActivationEvidence is not NativeWakeEvidence evidence
            || !ReferenceEquals(pendingWakeUtterance, utterance)
            || !ReferenceEquals(evidence.Request, request))
        {
            return false;
        }
        var current = !disposed && !profileSwitchActive && !profileRetirementFailed
            && cancellation is { IsCancellationRequested: false }
            && evidence.Generation == ambientGeneration
            && admission == evidence.Admission && IsObservedAdmissionCurrent(observation)
            && captureClosed.Task.IsCompletedSuccessfully && session is null && activatedQuiescent;
        if (!current) { pendingWakeUtterance = null; }
        return current;
    }

    private sealed class NativeWakeEvidence(
        HandsFreeVoiceAdmission admission, long generation, HostRequest request) : IVoiceActivationEvidence
    {
        public long Generation { get; } = generation;
        internal HandsFreeVoiceAdmission Admission { get; } = admission;
        internal HostRequest Request { get; } = request;
    }

    private sealed record AdmissionObservation(HandsFreeVoiceAdmission Expected, WakeWordObservation? Wake,
        long Generation, HandsFreeVoiceAdmission? Current, bool CaptureAllowed);

    private AdmissionObservation? ObserveAdmission(bool requireCaptureOwner = false)
    {
        HandsFreeVoiceAdmission expected;
        WakeWordObservation? wake;
        long generation;
        lock (gate)
        {
            if (admission is null || disposed || profileSwitchActive || profileRetirementFailed
                || cancellation is not { IsCancellationRequested: false }) { return null; }
            expected = admission;
            wake = admittedWake;
            generation = ambientGeneration;
        }
        // Host observation can acquire configuration locks. Recheck this exact phase after it returns.
        var captureAllowed = !requireCaptureOwner || canOpen();
        return new(expected, wake, generation, observe!(), captureAllowed);
    }

    private bool IsObservedAdmissionCurrent(AdmissionObservation? observation) =>
        observation is { CaptureAllowed: true } && !disposed && !profileSwitchActive && !profileRetirementFailed
        && cancellation is { IsCancellationRequested: false }
        && ambientGeneration == observation.Generation && ReferenceEquals(admission, observation.Expected)
        && admittedWake == observation.Wake && (observation.Wake is null
            ? observation.Expected.HasSameListenerAuthority(observation.Current)
            : observation.Current == observation.Expected);

    private bool IsCaptureAdmissionCurrent(long generation, CancellationToken token)
    {
        if (token.IsCancellationRequested) { return false; }
        var observation = ObserveAdmission(requireCaptureOwner: true);
        lock (gate)
        {
            return !token.IsCancellationRequested && ambientGeneration == generation
                && IsObservedAdmissionCurrent(observation);
        }
    }

    private void AdmitWake(WakeWordObservation wake, long generation, CancellationToken token)
    {
        var observation = ObserveAdmission(requireCaptureOwner: true);
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            if (observation?.Current is not { } activatedAdmission
                || ambientGeneration != generation || !IsObservedAdmissionCurrent(observation))
            {
                throw new InvalidOperationException("Original hands-free authority changed at activation.");
            }
            if (wake.Generation != generation || wake.CommandStartSample < 0 || !wake.PlaybackExcluded
                || admittedWake is not null && admittedWake != wake)
            {
                throw new InvalidOperationException("Wake observation was stale or playback exclusion was unconfirmed.");
            }
            if (admittedWake is not null) { return; }
            admission = activatedAdmission;
            admittedWake = wake;
            state = HandsFreeVoiceState.CapturingCommand;
        }
        commandActivated(generation);
        RequireCurrent(generation, token);
        PublishStateChanged(HandsFreeVoiceState.CapturingCommand);
    }

    private void RequireCurrent(long generation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsCaptureAdmissionCurrent(generation, token))
        {
            throw new InvalidOperationException("Voice admission changed; no command may be replayed.");
        }
    }

    private async Task CloseCaptureAsync()
    {
        IHandsFreeAudioSession? closing;
        lock (gate) { closing = session; }
        try
        {
            if (closing is not null)
            {
                closing.Invalidate();
                await closing.DisposeAsync();
                if (!closing.IsQuiescent) { throw new InvalidOperationException("Wake capture release was not confirmed."); }
            }
            lock (gate) { session = null; captureClosed.TrySetResult(); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lock (gate) { captureClosed.TrySetException(exception); }
            throw;
        }
    }

    private void SetState(HandsFreeVoiceState value)
    {
        lock (gate) { state = value; }
        PublishStateChanged(value);
    }

    private void PublishStateChanged(HandsFreeVoiceState value)
    {
        LifecycleStateChanged(logger, value);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static TaskCompletionSource CompletedReceipt()
    {
        var receipt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        receipt.SetResult();
        return receipt;
    }

    private void EnsureNotDisposed()
    {
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); }
    }

#pragma warning disable VSTHRD003 // Exposes only the owned worker receipt for deterministic lifecycle observation.
    internal Task Completion { get { lock (gate) { return run; } } }
#pragma warning restore VSTHRD003

    public async Task PauseWakeForReplyAsync()
    {
        if (State != HandsFreeVoiceState.WakeListening) { return; }
        await StopAsync();
#pragma warning disable VSTHRD003 // Wake-only retirement cannot be awaiting an operation or reply.
        await Completion;
#pragma warning restore VSTHRD003
    }

    public async Task ResumeAfterActivatedInputAsync()
    {
        lock (gate)
        {
            if (!audio.IsAvailable || disposed || profileSwitchActive || profileRetirementFailed || !run.IsCompleted
                || state == HandsFreeVoiceState.RecoveryRequired || !captureClosed.Task.IsCompletedSuccessfully) { return; }
        }
        if (observe?.Invoke() is null || !canOpen() || !activated.IsCaptureQuiescent) { return; }
        await StartHandsFreeAsync();
    }

    public void RequireExplicitRecovery()
    {
        InvalidateCapture();
        SetState(HandsFreeVoiceState.RecoveryRequired);
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
        }
        try
        {
            await StopAsync();
        }
        finally
        {
            try
            {
#pragma warning disable VSTHRD003 // Disposal joins the sole owned lifecycle worker after closing its input.
                await run;
#pragma warning restore VSTHRD003
            }
            finally
            {
                lock (gate)
                {
                    cancellation?.Dispose();
                    cancellation = null;
                }
            }
        }
    }
}