using System.Diagnostics;
using System.Runtime.InteropServices;

using Kora.Core.Platform;
using Kora.Core.Diagnostics;
using Kora.Core.Voice;
using Kora.Windows.Diagnostics;
using Kora.Windows.Session;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using NAudio.CoreAudioApi;

namespace Kora.Windows.Audio;

public sealed class WindowsVoiceRecognitionService : IActivatedVoiceRecognitionService
{
    private const float MinimumConfidence = 0.62f;
    private readonly Lock gate = new();
    private readonly SemaphoreSlim lifecycleLock = new(1, 1);
    private readonly ILogger<WindowsVoiceRecognitionService> logger;
    private readonly IActivatedCaptureFactory factory;
    private readonly VoiceCaptureLimits limits;
    private readonly HashSet<Task> pendingResourceOperations = [];
    private readonly bool ownsPrivacy;
    private IWindowsPrivacyObservationService? privacy;
    private IActivatedCapture? capture;
    private IActivatedCapture? retiringCapture;
    private CaptureCallbacks? callbacks;
    private CancellationTokenSource? openingCancellation;
    private long? pendingBeginGeneration;
    private BlockingAudioStream? audioStream;
    private MicrophoneDevice? selectedMicrophone;
    private MicrophoneDevice? resultMicrophone;
    private Timer? maximumCaptureTimer;
    private Timer? emptySpeechTimer;
    private long generation;
    private bool captureAdmitted;
    private bool acceptingAudio;
    private bool recordingStarted;
    private bool reportedListening;
    private long reportedGeneration;
    private bool transcriptDelivered;
    private bool disposed;
    private bool resourceCleanupFailed;
    private int stopRequests;
    private long activationGeneration;
    private long completedGeneration = -1;
    private bool transcriptPublicationReady;
    private VoiceTranscriptEventArgs? pendingTranscript;
    private VoiceRecognitionCompletionReason? completionReason;
    private long buffersClearedTimestamp;
    private Func<HostActivity>? beginCaptureReceipt;

    public WindowsVoiceRecognitionService(
        ILogger<WindowsVoiceRecognitionService> logger,
        IWindowsPrivacyObservationService? privacy = null)
        : this(logger, privacy, new WindowsActivatedCaptureFactory(), VoiceCaptureLimits.Default)
    {
    }

    internal WindowsVoiceRecognitionService(
        ILogger<WindowsVoiceRecognitionService> logger,
        IWindowsPrivacyObservationService? privacy,
        IActivatedCaptureFactory factory,
        VoiceCaptureLimits limits)
    {
        this.logger = logger;
        this.privacy = privacy;
        this.factory = factory;
        this.limits = limits;
        ownsPrivacy = privacy is null;
        if (privacy is not null)
        {
            privacy.Changed += OnPrivacyChanged;
        }
    }

    public event EventHandler<VoiceTranscriptEventArgs>? TranscriptRecognized;

    public event EventHandler<VoiceRecognitionFailureEventArgs>? RecognitionFailed;

    public event EventHandler<VoiceCaptureStateChangedEventArgs>? CaptureStateChanged;

    public event EventHandler<VoiceRecognitionCompletedEventArgs>? RecognitionCompleted;

    public event EventHandler<CapturePrivacyReceiptEventArgs>? CapturePrivacyMeasured;

    public bool IsAmbientListeningAvailable => false;

    public bool IsCaptureQuiescent
    {
        get
        {
            lock (gate)
            {
                return capture is null && !pendingBeginGeneration.HasValue && stopRequests == 0 && !resourceCleanupFailed &&
                    pendingResourceOperations.All(task => task.IsCompletedSuccessfully);
            }
        }
    }

    public long CaptureGeneration => Interlocked.Read(ref generation);

    public long Generation => CaptureGeneration;

    public bool IsListening
    {
        get
        {
            lock (gate)
            {
                return captureAdmitted && acceptingAudio && recordingStarted;
            }
        }
    }

    public IReadOnlyList<MicrophoneDevice> GetMicrophones()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = new List<MicrophoneDevice>();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (endpoint)
                {
                    devices.Add(new MicrophoneDevice(endpoint.ID, endpoint.FriendlyName));
                }
            }

            WindowsLog.DevicesEnumerated(logger, devices.Count, "microphone");
            return devices.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (COMException exception)
        {
            WindowsLog.Error(logger, exception, "Enumerating Windows microphone input");
            throw new InvalidOperationException("Windows could not enumerate microphone input devices.", exception);
        }
    }

    public MicrophoneDevice? GetDefaultMicrophone()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            return endpoint.State == DeviceState.Active ? new MicrophoneDevice(endpoint.ID, endpoint.FriendlyName) : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>Compatibility entry point for an explicit activated invocation, not ambient listening.</summary>
    public Task StartAsync(
        MicrophoneDevice microphone,
        IEnumerable<string> phrases,
        string? assistantName = null,
        CancellationToken cancellationToken = default) =>
        BeginPushToTalkAsync(microphone, phrases, assistantName, cancellationToken);

    public async Task BeginPushToTalkAsync(
        MicrophoneDevice microphone,
        IEnumerable<string> phrases,
        string? assistantName = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(microphone);
        ArgumentNullException.ThrowIfNull(phrases);
        cancellationToken.ThrowIfCancellationRequested();
        var commandPhrases = phrases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        long requestedGeneration;
        lock (gate)
        {
            if (stopRequests != 0)
            {
                throw new InvalidOperationException("Capture is closing. Wait for confirmed stop before activating again.");
            }

            if (captureAdmitted || pendingBeginGeneration.HasValue)
            {
                throw new InvalidOperationException("An activated capture is already in progress.");
            }

            if (resourceCleanupFailed || pendingResourceOperations.Any(task => !task.IsCompletedSuccessfully))
            {
                throw new InvalidOperationException("Prior native capture cleanup is not confirmed. Explicit recovery must wait for quiescence.");
            }

            requestedGeneration = Interlocked.Increment(ref generation);
            activationGeneration = requestedGeneration;
            transcriptPublicationReady = false;
            pendingTranscript = null;
            completionReason = null;
            pendingBeginGeneration = requestedGeneration;
            beginCaptureReceipt = HostActivity.CaptureContinuation(HostActivityLayer.Windows, HostOperation.Runtime);
        }

        CancellationTokenSource? openCancellation = null;
        CancellationTokenRegistration openRegistration = default;
        var lifecycleAcquired = false;
        var started = false;
        try
        {
            await AcquireLifecycleLockAsync(cancellationToken);
            lifecycleAcquired = true;
            ObjectDisposedException.ThrowIf(disposed, this);
            RequireGeneration(requestedGeneration);
            await Task.Run(DisposeCaptureAsync, CancellationToken.None);
            var openStarted = Stopwatch.GetTimestamp();
            openCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            openRegistration = openCancellation.Token.Register(() => InvalidateGeneration(requestedGeneration));
            lock (gate)
            {
                openingCancellation = openCancellation;
            }

            var observer = await Task.Run(EnsurePrivacyObserver, CancellationToken.None)
                .WaitAsync(RemainingOpenTime(openStarted), openCancellation.Token);
            var beforeOpen = await Task.Run(observer.Refresh, CancellationToken.None)
                .WaitAsync(RemainingOpenTime(openStarted), openCancellation.Token);
            RequireMicrophone(beforeOpen, microphone);
            RequireGeneration(requestedGeneration);
            var stream = new BlockingAudioStream();
            lock (gate)
            {
                RequireGeneration(requestedGeneration);
                audioStream = stream;
                selectedMicrophone = microphone;
                resultMicrophone = microphone;
            }
            var opening = Task.Run(() => factory.OpenAsync(microphone, commandPhrases, stream,
                () =>
                {
                    var snapshot = observer.Refresh();
                    return CaptureGeneration == requestedGeneration && !openCancellation.IsCancellationRequested &&
                        snapshot.TopologyRevision == beforeOpen.TopologyRevision && snapshot.CanCaptureFrom(microphone);
                }), CancellationToken.None);
            IActivatedCapture opened;
            try
            {
                opened = await opening.WaitAsync(RemainingOpenTime(openStarted), openCancellation.Token);
            }
            catch
            {
                TrackResourceOperation(DisposeLateOpenAsync(opening, stream));
                throw;
            }

            lock (gate)
            {
                capture = opened;
                RequireGeneration(requestedGeneration);
            }
            var afterOpen = await Task.Run(observer.Refresh, CancellationToken.None)
                .WaitAsync(RemainingOpenTime(openStarted), openCancellation.Token);
            RequireGeneration(requestedGeneration);
            RequireMicrophone(afterOpen, microphone);
            if (beforeOpen.TopologyRevision != afterOpen.TopologyRevision)
            {
                throw new InvalidOperationException("Audio devices changed while opening capture. Refresh and enable explicitly.");
            }

            callbacks = new CaptureCallbacks(this, opened, requestedGeneration);
            lock (gate)
            {
                RequireGeneration(requestedGeneration);
                openCancellation.Token.ThrowIfCancellationRequested();
                captureAdmitted = true;
                acceptingAudio = true;
                recordingStarted = false;
                transcriptDelivered = false;
                maximumCaptureTimer = new Timer(_ => EndBoundedCapture(requestedGeneration), null,
                    limits.MaximumCapture, Timeout.InfiniteTimeSpan);
                emptySpeechTimer = new Timer(_ => AbandonEmptyCapture(requestedGeneration), null,
                    limits.EmptySpeechDeadline, Timeout.InfiniteTimeSpan);
            }

            var starting = Task.Run(() =>
            {
                opened.Start();
                lock (gate)
                {
                    RequireGeneration(requestedGeneration);
                    recordingStarted = true;
                }

                NotifyCaptureState();
            }, CancellationToken.None);
            try
            {
                await starting.WaitAsync(RemainingOpenTime(openStarted), openCancellation.Token);
            }
            catch
            {
                TrackResourceOperation(ObserveLateStartAsync(starting));
                throw;
            }
            lock (gate)
            {
                RequireGeneration(requestedGeneration);
            }
            started = true;
            WindowsLog.VoiceActivationStarted(logger, commandPhrases.Length);
            _ = ObserveCompletionAsync(opened, requestedGeneration);
        }
        catch (COMException exception)
        {
            WindowsLog.Error(logger, exception, "Opening local activated microphone capture");
            throw new InvalidOperationException("Windows could not open the selected microphone.", exception);
        }
        finally
        {
            lock (gate)
            {
                openingCancellation = null;
                if (pendingBeginGeneration == requestedGeneration)
                {
                    pendingBeginGeneration = null;
                }
            }

            try
            {
                if (!started)
                {
                    InvalidateGeneration(requestedGeneration);
                    if (lifecycleAcquired)
                    {
                        _ = ObserveDetachedCleanupAsync(DisposeCaptureAsync());
                    }
                }
            }
            finally
            {
                try
                {
                    await openRegistration.DisposeAsync();
                    openCancellation?.Dispose();
                }
                finally
                {
                    if (lifecycleAcquired)
                    {
                        lifecycleLock.Release();
                    }
                }
            }
        }
    }

    public void InvalidateCapture()
    {
        long retiredGeneration;
        VoiceRecognitionCompletionReason reason;
        lock (gate)
        {
            retiredGeneration = activationGeneration;
            reason = completionReason switch
            {
                VoiceRecognitionCompletionReason.Failed => VoiceRecognitionCompletionReason.Failed,
                VoiceRecognitionCompletionReason.EmptySpeechTimeout => VoiceRecognitionCompletionReason.EmptySpeechTimeout,
                VoiceRecognitionCompletionReason.MaximumDuration => VoiceRecognitionCompletionReason.MaximumDuration,
                _ => VoiceRecognitionCompletionReason.Invalidated,
            };
            Interlocked.Increment(ref generation);
            captureAdmitted = false;
            acceptingAudio = false;
            recordingStarted = false;
            openingCancellation?.Cancel();
            maximumCaptureTimer?.Dispose();
            emptySpeechTimer?.Dispose();
            audioStream?.ClearAndComplete();
            buffersClearedTimestamp = Stopwatch.GetTimestamp();
            pendingTranscript = null;
            transcriptPublicationReady = false;
            // Initiate native release now; do not wait for UI dispatch or recognition cancellation.
            _ = capture?.ReleaseRecorder();
        }

        NotifyCaptureState();
        NotifyRecognitionCompleted(retiredGeneration, reason);
    }

    public bool AcceptCaptureGeneration(long generation)
    {
        lock (gate)
        {
            if (this.generation != generation || activationGeneration != generation ||
                pendingBeginGeneration.HasValue || disposed)
            {
                return false;
            }

            transcriptPublicationReady = true;
        }

        PublishPendingTranscript();
        return true;
    }

    public Task EndPushToTalkAsync(CancellationToken cancellationToken = default) =>
        EndPushToTalkAsync(expectedGeneration: null, cancellationToken);

    public Task EndCaptureAsync(CancellationToken cancellationToken = default) =>
        EndPushToTalkAsync(cancellationToken);

    private async Task EndPushToTalkAsync(long? expectedGeneration, CancellationToken cancellationToken)
    {
        IActivatedCapture? current;
        long currentGeneration;
        lock (gate)
        {
            if (expectedGeneration.HasValue && generation != expectedGeneration.Value)
            {
                return;
            }

            if (pendingBeginGeneration.HasValue)
            {
                InvalidateCapture();
                return;
            }

            if (!captureAdmitted)
            {
                return;
            }

            currentGeneration = generation;
            completionReason ??= VoiceRecognitionCompletionReason.PushToTalkReleased;
            acceptingAudio = false;
            recordingStarted = false;
            StopCaptureTimers();
            current = capture;
            audioStream?.Complete();
            _ = current?.ReleaseRecorder();
        }

        NotifyCaptureState();
        if (current is not null)
        {
            try
            {
                current.FinishRecognition(cancel: false);
                await AwaitRecognitionAsync(current, currentGeneration, cancellationToken);
            }
            catch
            {
                InvalidateGeneration(currentGeneration);
                await CleanupAsync(expectedCapture: current);
                throw;
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            stopRequests++;
        }

        InvalidateCapture();
        var acquired = false;
        try
        {
            await AcquireLifecycleLockAsync(cancellationToken);
            acquired = true;
            _ = DisposeCaptureAsync();
            await WaitForResourceQuiescenceAsync(cancellationToken);
        }
        finally
        {
            if (acquired)
            {
                lifecycleLock.Release();
            }

            lock (gate)
            {
                stopRequests--;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        try
        {
            await StopAsync();
        }
        finally
        {
            if (privacy is not null)
            {
                privacy.Changed -= OnPrivacyChanged;
                if (ownsPrivacy)
                {
                    privacy.Dispose();
                }
            }
        }
    }

    private IWindowsPrivacyObservationService EnsurePrivacyObserver()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (privacy is not null)
            {
                return privacy;
            }
        }

        var created = new WindowsPrivacyObservationService(
            new WindowsMicrophoneAccessService(NullLogger<WindowsMicrophoneAccessService>.Instance),
            NullLogger<WindowsPrivacyObservationService>.Instance);
        IWindowsPrivacyObservationService? effective;
        lock (gate)
        {
            if (!disposed && privacy is null)
            {
                privacy = created;
                privacy.Changed += OnPrivacyChanged;
            }

            effective = disposed ? null : privacy;
        }

        if (!ReferenceEquals(effective, created))
        {
            created.Dispose();
        }

        return effective ?? throw new ObjectDisposedException(nameof(WindowsVoiceRecognitionService));
    }

    private TimeSpan RemainingOpenTime(long started)
    {
        var remaining = limits.OpenDeadline - Stopwatch.GetElapsedTime(started);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void OnDataAvailable(IActivatedCapture sender, ReadOnlySpan<byte> buffer, long expected,
        Func<HostActivity> beginReceipt)
    {
        lock (gate)
        {
            if (!captureAdmitted || !acceptingAudio || generation != expected || !ReferenceEquals(sender, capture))
            {
                ReportStaleCallback("audio", expected, beginReceipt);
                return;
            }
        }

        var observed = privacy!.Current;
        lock (gate)
        {
            if (!captureAdmitted || !acceptingAudio || generation != expected || !ReferenceEquals(sender, capture))
            {
                ReportStaleCallback("audio", expected, beginReceipt);
                return;
            }

            if (selectedMicrophone is null || !observed.CanCaptureFrom(selectedMicrophone))
            {
                using var activity = beginReceipt();
                FailCapture("Windows privacy prerequisites no longer permit capture.",
                    VoiceRecognitionFailureReason.PrivacyTransition);
                activity.Complete(HostOperationOutcome.Failed);
            }
            else if (audioStream is not null && !audioStream.TryAdd(buffer))
            {
                using var activity = beginReceipt();
                FailCapture("Microphone audio exceeded the bounded local buffer.");
                activity.Complete(HostOperationOutcome.Failed);
            }
        }
    }

    private void OnTranscriptRecognized(object? sender, VoiceTranscriptEventArgs eventArgs, long expected)
    {
        long currentGeneration;
        lock (gate)
        {
            if (!captureAdmitted || transcriptDelivered || generation != expected || !ReferenceEquals(sender, capture))
            {
                ReportStaleCallback("transcript", expected);
                return;
            }

            currentGeneration = generation;
        }

        var observed = privacy!.Refresh();
        lock (gate)
        {
            if (!captureAdmitted || generation != currentGeneration || !ReferenceEquals(sender, capture))
            {
                ReportStaleCallback("transcript", expected);
                return;
            }

            if (selectedMicrophone is null || !observed.CanCaptureFrom(selectedMicrophone))
            {
                FailCapture("Windows privacy prerequisites no longer permit recognition.",
                    VoiceRecognitionFailureReason.PrivacyTransition);
                return;
            }

            if (eventArgs.Transcript.Length > limits.MaximumTranscriptCharacters)
            {
                FailCapture("The activated transcript exceeded the local size limit.");
                return;
            }

            if (eventArgs.Confidence >= MinimumConfidence && !string.IsNullOrWhiteSpace(eventArgs.Transcript))
            {
                transcriptDelivered = true;
                acceptingAudio = false;
                maximumCaptureTimer?.Dispose();
                emptySpeechTimer?.Dispose();
                audioStream?.ClearAndComplete();
                _ = capture?.ReleaseRecorder();
                NotifyCaptureState();
                WindowsLog.CommandRecognized(logger, eventArgs.Confidence);
                pendingTranscript = new VoiceTranscriptEventArgs(
                    eventArgs.Transcript, eventArgs.Confidence, currentGeneration);
                completionReason = VoiceRecognitionCompletionReason.Recognized;
            }
        }

        PublishPendingTranscript();
    }

    private void OnRecognitionFailed(object? sender, VoiceRecognitionFailureEventArgs eventArgs, long expected)
    {
        lock (gate)
        {
            if (captureAdmitted && generation == expected && ReferenceEquals(sender, capture))
            {
                FailCapture("Local activated speech recognition failed.");
            }
        }
    }

    private void OnSpeechDetected(object? sender, EventArgs eventArgs, long expected)
    {
        lock (gate)
        {
            if (captureAdmitted && generation == expected && ReferenceEquals(sender, capture))
            {
                emptySpeechTimer?.Dispose();
            }
        }
    }

    private void OnPrivacyChanged(object? sender, WindowsPrivacyChangedEventArgs eventArgs)
    {
        lock (gate)
        {
            if (!eventArgs.Current.CanCapture || resultMicrophone is not null &&
                !eventArgs.Current.CanCaptureFrom(resultMicrophone))
            {
                var recorder = capture ?? retiringCapture;
                var retiredGeneration = activationGeneration;
                var wasRecording = captureAdmitted && acceptingAudio && recordingStarted;
                var hadPendingOpen = pendingBeginGeneration.HasValue;
                if (captureAdmitted || selectedMicrophone is not null)
                {
                    FailCapture("Windows session, permission or microphone availability changed; this activation was retired.",
                        VoiceRecognitionFailureReason.PrivacyTransition);
                }
                else
                {
                    InvalidateCapture();
                }

                var cleared = buffersClearedTimestamp;
                var remaining = audioStream?.BufferedBytes ?? 0;
                var beginReceipt = HostActivity.CaptureContinuation(HostActivityLayer.Windows, HostOperation.Runtime);
                TrackResourceOperation(MeasurePrivacyReleaseAsync(recorder, eventArgs, retiredGeneration,
                    wasRecording, hadPendingOpen, cleared, remaining, beginReceipt));
            }
        }
    }

    private async Task MeasurePrivacyReleaseAsync(IActivatedCapture? recorder,
        WindowsPrivacyChangedEventArgs change, long retiredGeneration, bool wasRecording,
        bool hadPendingOpen, long cleared, int remaining, Func<HostActivity> beginReceipt)
    {
        var confirmed = false;
        Exception? failure = null;
        try
        {
            if (recorder is not null)
            {
                await recorder.ReleaseRecorder().ConfigureAwait(false);
                confirmed = true;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = exception;
            lock (gate)
            {
                resourceCleanupFailed = true;
            }
        }

        using var activity = beginReceipt();
        var receipt = new CapturePrivacyReceiptEventArgs(change.Observation, retiredGeneration,
            change.Current.SessionState, wasRecording, recorder is not null, hadPendingOpen, cleared, remaining,
            recorder?.RecorderReleasedTimestamp, confirmed);
        WindowsLog.CapturePrivacyReleased(logger, receipt.Observation.Id, receipt.Generation,
            receipt.WasRecording, receipt.HadRecorder, receipt.HadPendingOpen, receipt.BuffersClearedTimestamp,
            receipt.BufferedBytesAfterClear, receipt.RecorderReleasedTimestamp, receipt.ReleaseConfirmed,
            receipt.ObservedToReleaseMilliseconds, receipt.LockReleaseWithinTarget);
        Notify(CapturePrivacyMeasured, receipt);
        if (failure is not null)
        {
            WindowsLog.Error(logger, failure, "Measuring native recorder release after a privacy change");
        }
        activity.Complete(failure is null ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
    }

    private void ReportStaleCallback(string kind, long expected, Func<HostActivity>? beginReceipt = null)
    {
        using var activity = beginReceipt is not null ? beginReceipt()
            : HostActivity.BeginOperation(HostActivityLayer.Windows, HostOperation.Runtime);
        WindowsLog.StaleCaptureCallback(logger, kind, expected, generation);
        activity.Complete(HostOperationOutcome.Completed);
    }

    private void FailCapture(string message,
        VoiceRecognitionFailureReason reason = VoiceRecognitionFailureReason.CaptureFailure)
    {
        using var activity = HostActivity.Current is null && beginCaptureReceipt is not null
            ? beginCaptureReceipt()
            : HostActivity.BeginOperation(HostActivityLayer.Windows, HostOperation.Runtime);
        var failedGeneration = CaptureGeneration;
        lock (gate)
        {
            completionReason = VoiceRecognitionCompletionReason.Failed;
        }

        InvalidateCapture();
        Notify(RecognitionFailed, new VoiceRecognitionFailureEventArgs(message, failedGeneration, reason));
        _ = ObserveDetachedCleanupAsync(CleanupAsync(CaptureGeneration));
        activity.Complete(HostOperationOutcome.Failed);
    }

    private void InvalidateGeneration(long expected)
    {
        lock (gate)
        {
            if (generation == expected)
            {
                InvalidateCapture();
            }
        }
    }

    private void RequireGeneration(long expected)
    {
        if (CaptureGeneration != expected)
        {
            throw new OperationCanceledException("Capture was invalidated while opening.");
        }
    }

    private static void RequireMicrophone(WindowsPrivacySnapshot observed, MicrophoneDevice microphone)
    {
        var endpointId = microphone.IsSystemDefault ? observed.DefaultMicrophoneId : microphone.Id;
        if (endpointId is null || !observed.ActiveMicrophoneIds.Contains(endpointId, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(microphone), "The selected microphone is no longer available.");
        }

        if (!observed.CanCapture)
        {
            throw new InvalidOperationException("Capture requires an authoritative unlocked Windows session and confirmed microphone permission.");
        }
    }

    private void EndBoundedCapture(long expected)
    {
        lock (gate)
        {
            if (generation != expected || !captureAdmitted)
            {
                return;
            }

            completionReason ??= VoiceRecognitionCompletionReason.MaximumDuration;
        }

        _ = EndBoundedCaptureAsync(expected);
    }

    private async Task EndBoundedCaptureAsync(long expected)
    {
        try
        {
            await EndPushToTalkAsync(expected, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WindowsLog.Error(logger, exception, "Ending bounded activated capture");
            InvalidateGeneration(expected);
        }
    }

    private void AbandonEmptyCapture(long expected)
    {
        lock (gate)
        {
            if (generation == expected && captureAdmitted)
            {
                completionReason = VoiceRecognitionCompletionReason.EmptySpeechTimeout;
                InvalidateCapture();
                _ = ObserveDetachedCleanupAsync(CleanupAsync(CaptureGeneration));
            }
        }
    }

    private async Task ObserveCompletionAsync(IActivatedCapture current, long expected)
    {
        try
        {
#pragma warning disable VSTHRD003 // Completion is raised by a local recognition callback.
            await current.Completion;
#pragma warning restore VSTHRD003
            CompleteGeneration(expected);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WindowsLog.Error(logger, exception, "Completing local activated recognition");
            InvalidateGeneration(expected);
        }

        await ObserveDetachedCleanupAsync(CleanupAsync(expectedCapture: current));
    }

    private async Task AwaitRecognitionAsync(IActivatedCapture current, long expected, CancellationToken cancellationToken)
    {
        try
        {
#pragma warning disable VSTHRD003 // Completion is raised by a local recognition callback.
            await current.Completion.WaitAsync(limits.RecognitionDeadline, cancellationToken);
#pragma warning restore VSTHRD003
        }
        catch (TimeoutException)
        {
            WindowsLog.Warning(logger, "Activated recognition did not complete within its teardown deadline");
            InvalidateGeneration(expected);
        }
        catch (OperationCanceledException)
        {
            InvalidateGeneration(expected);
            throw;
        }
        finally
        {
            CompleteGeneration(expected);
            await CleanupAsync(expectedCapture: current);
        }
    }

    private async Task CleanupAsync(long? expectedGeneration = null, IActivatedCapture? expectedCapture = null)
    {
        // Recorder release has already started; slower native recognition teardown leaves the event thread.
        await Task.Yield();
        await AcquireLifecycleLockAsync(CancellationToken.None);
        try
        {
            if ((!expectedGeneration.HasValue || CaptureGeneration == expectedGeneration.Value) &&
                (expectedCapture is null || ReferenceEquals(capture, expectedCapture)))
            {
                await DisposeCaptureAsync();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lock (gate)
            {
                resourceCleanupFailed = true;
            }

            WindowsLog.Error(logger, exception, "Releasing activated speech resources");
            throw;
        }
        finally
        {
            lifecycleLock.Release();
        }
    }

    private void CompleteGeneration(long expected)
    {
        VoiceRecognitionCompletionReason? terminalReason = null;
        lock (gate)
        {
            if (generation == expected)
            {
                captureAdmitted = false;
                acceptingAudio = false;
                recordingStarted = false;
                maximumCaptureTimer?.Dispose();
                emptySpeechTimer?.Dispose();
                audioStream?.ClearAndComplete();
                _ = capture?.ReleaseRecorder();
                if (pendingTranscript is null)
                {
                    terminalReason = completionReason ?? VoiceRecognitionCompletionReason.NoSpeechRecognized;
                }
            }

            NotifyCaptureState();
        }

        if (terminalReason is { } reason)
        {
            NotifyRecognitionCompleted(expected, reason);
        }
    }

    private async Task DisposeLateOpenAsync(Task<IActivatedCapture> opening, BlockingAudioStream stream)
    {
        try
        {
#pragma warning disable VSTHRD003 // Noncancellable native opens are quarantined and disposed on late completion.
            var late = await opening;
#pragma warning restore VSTHRD003
            try
            {
                await late.ReleaseRecorder();
            }
            finally
            {
                await late.DisposeAsync();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WindowsLog.Error(logger, exception, "Releasing a cancelled microphone open");
            if (exception is not OperationCanceledException)
            {
                lock (gate)
                {
                    resourceCleanupFailed = true;
                }
            }
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    private async Task ObserveLateStartAsync(Task starting)
    {
        try
        {
#pragma warning disable VSTHRD003 // A late synchronous native start is quarantined, never allowed to restore its generation.
            await starting;
#pragma warning restore VSTHRD003
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WindowsLog.Error(logger, exception, "Completing an invalidated native recording start");
        }
    }

    private async Task ObserveDetachedCleanupAsync(Task releasing)
    {
        try
        {
#pragma warning disable VSTHRD003 // Cleanup has already detached and cleared its generation-owned resources.
            await releasing;
#pragma warning restore VSTHRD003
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WindowsLog.Error(logger, exception, "Releasing failed activated capture resources");
        }
    }

    private Task DisposeCaptureAsync()
    {
        IActivatedCapture? current;
        BlockingAudioStream? stream;
        Task releasing;
        lock (gate)
        {
            current = capture;
            if (current is not null)
            {
                retiringCapture = current;
            }
            stream = audioStream;
            capture = null;
            callbacks?.Unsubscribe();
            callbacks = null;
            audioStream = null;
            selectedMicrophone = null;
            StopCaptureTimers();
            maximumCaptureTimer = null;
            emptySpeechTimer = null;
            stream?.ClearAndComplete();
            releasing = Task.Run(() => ReleaseCaptureResourcesAsync(current, stream), CancellationToken.None);
            TrackResourceOperation(releasing);
        }

        return releasing;
    }

    private async Task ReleaseCaptureResourcesAsync(IActivatedCapture? current, BlockingAudioStream? stream)
    {
        await Task.Yield();
        try
        {
            if (current is not null)
            {
                try
                {
                    await current.ReleaseRecorder();
                }
                finally
                {
                    await current.DisposeAsync();
                }
            }
        }
        finally
        {
            if (stream is not null)
            {
                await stream.DisposeAsync();
            }
        }
        lock (gate)
        {
            if (ReferenceEquals(retiringCapture, current))
            {
                retiringCapture = null;
            }
        }
    }

    private void TrackResourceOperation(Task operation)
    {
        lock (gate)
        {
            pendingResourceOperations.Add(operation);
        }

        _ = ObserveResourceOperationAsync(operation);
    }

    private async Task ObserveResourceOperationAsync(Task operation)
    {
        try
        {
#pragma warning disable VSTHRD003 // Every task represents generation-owned native work, independent of the UI dispatcher.
            await operation;
#pragma warning restore VSTHRD003
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lock (gate)
            {
                resourceCleanupFailed = true;
            }

            WindowsLog.Error(logger, exception, "Confirming native capture resource release");
        }
        finally
        {
            lock (gate)
            {
                pendingResourceOperations.Remove(operation);
            }
        }
    }

    private async Task WaitForResourceQuiescenceAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            Task[] pending;
            lock (gate)
            {
                if (resourceCleanupFailed || pendingResourceOperations.Any(task => task.IsFaulted || task.IsCanceled))
                {
                    throw new InvalidOperationException("Native capture release could not be confirmed. Restart is required before handoff.");
                }

                pending = pendingResourceOperations.Where(task => !task.IsCompleted).ToArray();
            }

            if (pending.Length == 0)
            {
                return;
            }

            try
            {
                await Task.WhenAll(pending).WaitAsync(RemainingOpenTime(started), cancellationToken);
            }
            catch (TimeoutException exception)
            {
                throw new InvalidOperationException("Native capture work is still closing; quiescence is not confirmed.", exception);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                lock (gate)
                {
                    resourceCleanupFailed = true;
                }

                throw new InvalidOperationException("Native capture release failed; quiescence is not confirmed.", exception);
            }
        }
    }

    private async Task AcquireLifecycleLockAsync(CancellationToken cancellationToken)
    {
        if (await lifecycleLock.WaitAsync(limits.OpenDeadline, cancellationToken))
        {
            return;
        }

        lock (gate)
        {
            resourceCleanupFailed = true;
        }

        throw new InvalidOperationException(
            "Native capture lifecycle work did not finish within its deadline. Restart is required before using voice again.");
    }

    private void StopCaptureTimers()
    {
        maximumCaptureTimer?.Dispose();
        emptySpeechTimer?.Dispose();
    }

    private void NotifyCaptureState()
    {
        long observedGeneration;
        bool listening;
        lock (gate)
        {
            observedGeneration = generation;
            listening = captureAdmitted && acceptingAudio && recordingStarted;
            if (reportedGeneration == observedGeneration && reportedListening == listening)
            {
                return;
            }

            reportedGeneration = observedGeneration;
            reportedListening = listening;
        }

        Notify(CaptureStateChanged, new VoiceCaptureStateChangedEventArgs(observedGeneration, listening));
    }

    private void PublishPendingTranscript()
    {
        lock (gate)
        {
            if (!transcriptPublicationReady || pendingTranscript is null ||
                pendingTranscript.Generation != generation)
            {
                return;
            }
        }

        var observed = privacy!.Refresh();
        lock (gate)
        {
            if (!transcriptPublicationReady || pendingTranscript is not { } transcript ||
                transcript.Generation != generation)
            {
                return;
            }

            if (resultMicrophone is null || !observed.CanCaptureFrom(resultMicrophone))
            {
                FailCapture("Windows privacy prerequisites changed before transcript admission.",
                    VoiceRecognitionFailureReason.PrivacyTransition);
                return;
            }

            pendingTranscript = null;
            WindowsLog.CaptureTranscriptAdmitted(logger, transcript.Generation);
            Notify(TranscriptRecognized, transcript);
            NotifyRecognitionCompleted(transcript.Generation, VoiceRecognitionCompletionReason.Recognized);
        }
    }

    private void NotifyRecognitionCompleted(long completedActivation, VoiceRecognitionCompletionReason reason)
    {
        lock (gate)
        {
            if (completedActivation == 0 || completedActivation != activationGeneration ||
                completedGeneration == completedActivation)
            {
                return;
            }

            completedGeneration = completedActivation;
        }

        Notify(RecognitionCompleted, new VoiceRecognitionCompletedEventArgs(completedActivation, reason));
    }

    private void Notify<T>(EventHandler<T>? handlers, T args) where T : EventArgs
    {
        foreach (var callback in handlers?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<T>)callback)(this, args);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                WindowsLog.Error(logger, exception, "Notifying an activated recognition consumer");
            }
        }
    }

    private sealed class CaptureCallbacks
    {
        private readonly IActivatedCapture capture;
        private readonly ActivatedAudioAvailable data;
        private readonly EventHandler<VoiceTranscriptEventArgs> transcript;
        private readonly EventHandler<VoiceRecognitionFailureEventArgs> failed;
        private readonly EventHandler speech;

        public CaptureCallbacks(WindowsVoiceRecognitionService service, IActivatedCapture capture, long generation)
        {
            this.capture = capture;
            var begin = HostActivity.CaptureContinuation(HostActivityLayer.Windows, HostOperation.Runtime);
            data = (sender, buffer) => service.OnDataAvailable(sender, buffer, generation, begin);
            transcript = (sender, args) =>
            {
                using var activity = begin();
                service.OnTranscriptRecognized(sender, args, generation);
                activity.Complete(HostOperationOutcome.Completed);
            };
            failed = (sender, args) =>
            {
                using var activity = begin();
                service.OnRecognitionFailed(sender, args, generation);
                activity.Complete(HostOperationOutcome.Completed);
            };
            speech = (sender, args) => service.OnSpeechDetected(sender, args, generation);
            capture.DataAvailable += data;
            capture.TranscriptRecognized += transcript;
            capture.RecognitionFailed += failed;
            capture.SpeechDetected += speech;
        }

        public void Unsubscribe()
        {
            capture.DataAvailable -= data;
            capture.TranscriptRecognized -= transcript;
            capture.RecognitionFailed -= failed;
            capture.SpeechDetected -= speech;
        }
    }
}
