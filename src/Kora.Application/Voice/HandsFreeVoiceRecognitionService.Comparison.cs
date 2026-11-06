using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed partial class HandsFreeVoiceRecognitionService : ISpeechSetupComparisonRuntime
{
    private bool comparisonActive;
    private int activatedOpenRequests;

    public IReadOnlyList<SpeechComparisonProfile> GetComparisonProfiles() =>
        (activated as ISpeechSetupComparisonRuntime)?.GetComparisonProfiles() ?? [];

    public async Task<ISpeechSetupComparisonLease> AcquireComparisonAsync(SpeechInputGeneration generation,
        MicrophoneDevice microphone, Func<bool> eligible, CancellationToken cancellationToken)
    {
        if (!eligible()) { throw new InvalidOperationException("Comparison admission is unavailable."); }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (comparisonActive || profileSwitchActive || profileRetirementFailed || !IsCaptureQuiescent
                || !run.IsCompleted || activatedOpenRequests != 0 || activated is not ISpeechSetupComparisonRuntime)
            { throw new InvalidOperationException("Comparison requires unowned quiescent input. Finish current voice work first."); }
            comparisonActive = true;
        }
        try
        {
            var native = await ((ISpeechSetupComparisonRuntime)activated).AcquireComparisonAsync(
                generation, microphone, eligible, cancellationToken).ConfigureAwait(false);
            return new ComparisonLease(this, native);
        }
        catch
        {
            lock (gate) { comparisonActive = false; }
            throw;
        }
    }

    private sealed class ComparisonLease(HandsFreeVoiceRecognitionService owner, ISpeechSetupComparisonLease native)
        : ISpeechSetupComparisonLease
    {
        private Task? release;
        public Task<SpeechComparisonObservation> CaptureAsync(SpeechComparisonProfile profile, Action recordingStarted,
            CancellationToken cancellationToken)
        {
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(release is not null, this);
            }
            return native.CaptureAsync(profile, recordingStarted, cancellationToken);
        }
        public ValueTask DisposeAsync()
        {
            lock (owner.gate) { return new(release ??= ReleaseAsync()); }
        }
        private async Task ReleaseAsync()
        {
            await native.DisposeAsync().ConfigureAwait(false);
            lock (owner.gate) { owner.comparisonActive = false; }
        }
    }
}
