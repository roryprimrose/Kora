using Kora.Core.Voice;

namespace Kora.Windows.Audio;

public sealed partial class WindowsVoiceRecognitionService
{
    private bool comparisonActive;
    private ComparisonLease? comparisonLease;

    public IReadOnlyList<SpeechComparisonProfile> GetComparisonProfiles() => factory.GetComparisonProfiles();

    public Task<ISpeechSetupComparisonLease> AcquireComparisonAsync(SpeechInputGeneration generation,
        MicrophoneDevice microphone, Func<bool> eligible, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("Comparison admission is unavailable."); }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (profileSwitchActive || !profileAvailable || profileGeneration != generation
                || !IsCaptureQuiescent || GetComparisonProfiles().Count == 0)
            {
                throw new InvalidOperationException("Comparison requires exact admitted installed profiles and unowned quiescent input. No listening or preference was changed.");
            }
            comparisonActive = true;
            comparisonLease = new(this, generation, microphone, eligible, cancellationToken);
            return Task.FromResult<ISpeechSetupComparisonLease>(comparisonLease);
        }
    }

    private void CancelComparison()
    {
        lock (gate) { comparisonLease?.Cancel(); }
    }

    private sealed class ComparisonLease(WindowsVoiceRecognitionService owner, SpeechInputGeneration generation,
        MicrophoneDevice microphone, Func<bool> eligible, CancellationToken cancellationToken) : ISpeechSetupComparisonLease
    {
        private readonly CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        private Task<SpeechComparisonObservation>? attempt;
        private Task? release;
        private bool failedCleanup;
        private int attempts;

        public void Cancel() => lifetime.Cancel();

        public Task<SpeechComparisonObservation> CaptureAsync(SpeechComparisonProfile profile, Action recordingStarted,
            CancellationToken token)
        {
            if (!Current(profile)) { throw new InvalidOperationException("Comparison admission changed."); }
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(release is not null, this);
                if (attempt is { IsCompleted: false } || ++attempts > SpeechComparisonObservation.MaximumProfiles * 2)
                { throw new InvalidOperationException("Comparison profile/admission changed or a capture still owns input."); }
                attempt = Task.Run(() => CaptureCoreAsync(profile, recordingStarted, token), CancellationToken.None);
                return attempt;
            }
        }

        private bool Current(SpeechComparisonProfile profile) =>
            !lifetime.IsCancellationRequested && !owner.disposed && eligible()
            && owner.profileGeneration == generation && owner.GetComparisonProfiles().Contains(profile);

        private async Task<SpeechComparisonObservation> CaptureCoreAsync(SpeechComparisonProfile profile, Action recordingStarted,
            CancellationToken token)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var attemptStarted = owner.time.GetTimestamp();
            using var stream = new BlockingAudioStream();
            var observer = owner.EnsurePrivacyObserver();
            var baseline = observer.Refresh();
            var final = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            IActivatedCapture? native = null;
            var accepting = 1;
            bool Admitted() => Volatile.Read(ref accepting) == 1 && !linked.IsCancellationRequested && Current(profile)
                && owner.time.GetElapsedTime(attemptStarted) < SpeechComparisonObservation.MaximumAttempt
                && observer.Current.TopologyRevision == baseline.TopologyRevision
                && observer.Current.CanCaptureFrom(microphone);
            void Retire()
            {
                if (Interlocked.Exchange(ref accepting, 0) == 0) { return; }
                stream.ClearAndComplete();
                linked.Cancel();
            }
            void PrivacyChanged(object? sender, Kora.Core.Platform.WindowsPrivacyChangedEventArgs args) => Retire();
            void Audio(IActivatedCapture sender, ReadOnlySpan<byte> buffer)
            {
                if (!ReferenceEquals(sender, native)) { return; }
                if (!Admitted() || !stream.TryAdd(buffer)) { Retire(); }
            }
            void Transcript(object? sender, VoiceTranscriptEventArgs args)
            {
                if (!ReferenceEquals(sender, native)) { return; }
                if (!Admitted()) { Retire(); return; }
                if (args.Transcript.Length > SpeechComparisonObservation.MaximumFinalTextLength)
                {
                    final.TrySetException(new InvalidOperationException("Comparison final text exceeded the in-memory limit."));
                    return;
                }
                final.TrySetResult(args.Transcript);
            }
            void Failed(object? sender, VoiceRecognitionFailureEventArgs args)
            {
                if (ReferenceEquals(sender, native) && Admitted())
                { final.TrySetException(new InvalidOperationException("Local comparison recognition failed. Review input and retry.")); }
            }
            observer.Changed += PrivacyChanged;
            using var deadline = owner.time.CreateTimer(_ => Retire(), null,
                SpeechComparisonObservation.MaximumAttempt, Timeout.InfiniteTimeSpan);
            using var revalidation = owner.time.CreateTimer(_ => { if (!Admitted()) { Retire(); } }, null,
                TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
            using var cancellation = linked.Token.Register(stream.ClearAndComplete);
            try
            {
                if (!Admitted()) { throw new InvalidOperationException("Microphone/owner/privacy admission is unavailable."); }
                var started = owner.time.GetTimestamp();
                // Expected text is deliberately absent: no command grammar, routing subscription or source context.
                native = await owner.factory.OpenAsync(microphone, [], stream, Admitted,
                    SpeechComparisonObservation.MaximumAttempt, new(generation.Number, profile.Selection)).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                if (!Admitted()) { throw new OperationCanceledException(linked.Token); }
                native.DataAvailable += Audio;
                native.TranscriptRecognized += Transcript;
                native.RecognitionFailed += Failed;
                native.Start();
                var openToStart = owner.time.GetElapsedTime(started);
                if (!Admitted()) { throw new OperationCanceledException(linked.Token); }
                recordingStarted();
                var completed = await Task.WhenAny(final.Task, native.Completion).WaitAsync(linked.Token).ConfigureAwait(false);
                if (completed != final.Task && !final.Task.IsCompleted)
                { throw new InvalidOperationException("No final text was recognized. Review the microphone and retry."); }
                var text = await final.Task.ConfigureAwait(false);
                if (!Admitted()) { throw new OperationCanceledException(linked.Token); }
                return new(text, openToStart);
            }
            finally
            {
                Interlocked.Exchange(ref accepting, 0);
                observer.Changed -= PrivacyChanged;
                stream.ClearAndComplete();
                if (native is not null)
                {
                    native.DataAvailable -= Audio;
                    native.TranscriptRecognized -= Transcript;
                    native.RecognitionFailed -= Failed;
                    await CleanupAsync(native).ConfigureAwait(false);
                }
            }
        }

        private async Task CleanupAsync(IActivatedCapture native)
        {
            try
            {
                try
                {
                    await native.ReleaseRecorder().ConfigureAwait(false);
                    native.FinishRecognition(cancel: true);
                }
                finally { await native.DisposeAsync().ConfigureAwait(false); }
            }
            catch
            {
                failedCleanup = true;
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (owner.gate) { return new(release ??= ReleaseAsync()); }
        }

        private async Task ReleaseAsync()
        {
            Cancel();
            try
            {
                if (attempt is not null)
                {
#pragma warning disable VSTHRD003 // Joins the exact owned native attempt, including a late open.
                    try { await attempt.ConfigureAwait(false); }
#pragma warning restore VSTHRD003
                    catch (OperationCanceledException) { }
                    catch (InvalidOperationException) when (!failedCleanup) { }
                }
            }
            finally
            {
                lock (owner.gate)
                {
                    owner.resourceCleanupFailed |= failedCleanup;
                    owner.comparisonActive = false;
                    owner.comparisonLease = null;
                    lifetime.Dispose();
                }
            }
        }
    }
}
