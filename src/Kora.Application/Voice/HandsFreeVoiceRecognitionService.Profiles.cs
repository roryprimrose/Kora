using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed partial class HandsFreeVoiceRecognitionService
{
    private bool profileSwitchActive;
    private bool profileRetirementFailed;

    public SpeechInputGeneration ProfileGeneration => ProfileRuntime.ProfileGeneration;
    public bool IsProfileAvailable
    {
        get { lock (gate) { return !disposed && !profileSwitchActive && !profileRetirementFailed && activated is ISpeechInputProfileRuntime runtime && runtime.IsProfileAvailable; } }
    }

    public void InitializeProfile(SpeechInputSelection? selection)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (profileSwitchActive || !IsCaptureQuiescent) { throw new InvalidOperationException("Profile initialization requires unowned quiescent input."); }
            ProfileRuntime.InitializeProfile(selection);
        }
    }

    public async Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (profileSwitchActive) { throw new InvalidOperationException("A speech-input profile switch already owns the runtime."); }
            profileSwitchActive = true;
        }
        try
        {
            await StopAsync(cancellationToken);
            EnsureNotDisposed();
            return new ProfileSwitch(this, await ProfileRuntime.RetireProfileAsync(cancellationToken));
        }
        catch
        {
            lock (gate) { profileSwitchActive = false; profileRetirementFailed = true; }
            throw;
        }
    }

    private ISpeechInputProfileRuntime ProfileRuntime => activated as ISpeechInputProfileRuntime
        ?? throw new InvalidOperationException("The activated adapter does not provide speech-input profile switching.");

    private sealed class ProfileSwitch(HandsFreeVoiceRecognitionService owner, ISpeechInputProfileSwitch native) : ISpeechInputProfileSwitch
    {
        private Task? release;
        public void Publish(SpeechInputGeneration generation)
        {
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(release is not null, this);
                native.Publish(generation);
                owner.profileRetirementFailed = false;
            }
        }
        public void HoldUnavailable()
        {
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(release is not null, this);
                owner.profileRetirementFailed = true;
                native.HoldUnavailable();
            }
        }
        public ValueTask DisposeAsync()
        {
            lock (owner.gate) { return new(release ??= ReleaseAsync()); }
        }
        private async Task ReleaseAsync()
        {
            await native.DisposeAsync();
            lock (owner.gate) { owner.profileSwitchActive = false; }
        }
    }
}
