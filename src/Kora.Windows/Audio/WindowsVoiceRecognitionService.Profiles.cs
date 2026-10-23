using Kora.Core.Diagnostics;
using Kora.Core.Voice;

namespace Kora.Windows.Audio;

public sealed partial class WindowsVoiceRecognitionService
{
    private SpeechInputGeneration profileGeneration = new(0, SpeechInputSelection.Default);
    private bool profileAvailable = true;
    private bool profileInitialized;
    private bool profileSwitchActive;

    public SpeechInputGeneration ProfileGeneration { get { lock (gate) { return profileGeneration; } } }
    public bool IsProfileAvailable { get { lock (gate) { return profileAvailable && !profileSwitchActive; } } }

    public void InitializeProfile(SpeechInputSelection? selection)
    {
        selection?.Validate();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (profileInitialized || !IsCaptureQuiescent || profileSwitchActive || generation != 0)
            {
                throw new InvalidOperationException("Speech-input startup binding must precede capture and can run only once.");
            }
            profileInitialized = true;
            profileAvailable = selection is not null && SpeechInputProfileCatalogue.UnavailableReason(selection) is null;
            if (selection is not null) { profileGeneration = new(0, selection); }
        }
    }

    public async Task<ISpeechInputProfileSwitch> RetireProfileAsync(CancellationToken cancellationToken)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Runtime);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (profileSwitchActive) { throw new InvalidOperationException("A speech-input profile switch is already active."); }
            profileSwitchActive = true;
        }
        try
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                if (!IsCaptureQuiescent) { throw new InvalidOperationException("Capture resources did not confirm quiescence."); }
            }
            activity.Complete(HostOperationOutcome.Completed);
            return new ProfileSwitch(this);
        }
        catch (OperationCanceledException)
        {
            lock (gate) { profileAvailable = false; profileSwitchActive = false; }
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            lock (gate) { profileAvailable = false; profileSwitchActive = false; }
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private sealed class ProfileSwitch(WindowsVoiceRecognitionService owner) : ISpeechInputProfileSwitch
    {
        private bool released;
        private bool published;

        public void Publish(SpeechInputGeneration selection)
        {
            selection.Selection.Validate();
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(released || owner.disposed, this);
                if (published || !owner.IsCaptureQuiescent || selection.Number != checked(owner.profileGeneration.Number + 1))
                {
                    throw new InvalidOperationException("Profile publication requires the next exact generation and confirmed quiescence.");
                }
                owner.profileGeneration = selection;
                owner.profileAvailable = SpeechInputProfileCatalogue.UnavailableReason(selection.Selection) is null;
                published = true;
            }
        }

        public void HoldUnavailable()
        {
            lock (owner.gate)
            {
                ObjectDisposedException.ThrowIf(released, this);
                owner.profileAvailable = false;
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (owner.gate)
            {
                if (!released) { released = true; owner.profileSwitchActive = false; }
            }
            return ValueTask.CompletedTask;
        }
    }
}
