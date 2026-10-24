using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Application.Presentation;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AudioPlaybackSettings audioPlaybackSettings;
    private bool volumeControlActive;

    public int? SavedPlaybackVolume => audioPlaybackSettings.VolumeState?.Effective?.Percent;
    public IReadOnlyList<int> PlaybackVolumeChoices => audioPlaybackSettings.PlaybackVolumeChoices;
    public int SelectedPlaybackVolume
    {
        get => audioPlaybackSettings.SelectedPlaybackVolume;
        set
        {
            var previous = audioPlaybackSettings.SelectedPlaybackVolume;
            audioPlaybackSettings.SelectedPlaybackVolume = value;
            if (previous != value) { OnPropertyChanged(); }
        }
    }
    public AsyncCommand RefreshPlaybackVolumeCommand { get; }
    public AsyncCommand SavePlaybackVolumeCommand { get; }
    public AsyncCommand ResetPlaybackVolumeCommand { get; }
    public bool CanChangePlaybackVolume => audioPlaybackSettings.VolumeState is not null && IsCallMutationHostEligible
        && !volumeControlActive && !IsResponseInteractionPending;
    public string PlaybackVolumeStatus => audioPlaybackSettings.PlaybackVolumeStatus;

    private void OnAudioPlaybackOutputInvalidated(object? sender, EventArgs args)
    {
        if (IsSpeaking) { forceVisualResponse = true; }
    }

    private void OnAudioPlaybackSettingsChanged(object? sender, AudioPlaybackSetting setting)
    {
        if (setting == AudioPlaybackSetting.Volume) { SynchronizePlaybackVolume(); }
        else { SynchronizeWindowsSpeechRate(); }
    }

    private void SynchronizePlaybackVolume()
    {
        if (disposed) { return; }
        OnPropertyChanged(nameof(SelectedPlaybackVolume));
        OnPropertyChanged(nameof(PlaybackVolumeStatus));
        OnPropertyChanged(nameof(SavedPlaybackVolume));
        OnPropertyChanged(nameof(CanChangePlaybackVolume));
        NotifyOutputPolicyChanged();
    }

    internal async Task ExecutePlaybackVolumeCommandAsync(PlaybackVolumeCommand command, SecurityAuditInitiator initiator,
        CancellationToken cancellationToken = default)
    {
        if (!CanChangePlaybackVolume)
        {
            if (!disposed) { Transcript = "Volume control requires the owning unlocked host, admitted audio session and no pending question/approval."; }
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        {
            ShowFailure("Clarify the volume setting.", command.Error!);
            return;
        }
        var origin = OriginalOrigin(initiator);
        if (initiator == SecurityAuditInitiator.VoiceCommand && HostActivity.Current is null) { origin = RequestOrigin.ActivatedVoice; }
        var hostEligible = CaptureAudioControlEligibility(origin);
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var generation = voiceRecognition.Generation;
        bool Eligible() => hostEligible() && assistantNameConfiguration.Get().Revision == nameRevision
            && (origin != RequestOrigin.ActivatedVoice || voiceRecognition.Generation == generation);
        Func<bool> eligible = Eligible;
        var preserveResponse = IsSpeaking;
        volumeControlActive = true;
        OnPropertyChanged(nameof(CanChangePlaybackVolume));
        try
        {
            var change = await audioPlaybackSettings.ExecuteVolumeAsync(command, origin, initiator,
                communicationPolicy, eligible, cancellationToken);
            if (!eligible()) { return; }
            if (change.Saved && IsSpeaking) { await textToSpeech.StopAsync(CancellationToken.None); }
            if (!eligible()) { return; }
            var result = PlaybackVolumeState.Serialize(audioPlaybackSettings.VolumeState!, CallPolicyRevision, change.Outcome, change.PreviousDesired);
            if (preserveResponse)
            {
                Transcript = result;
                PreserveSpokenResponseFailure("Volume preference result is in status; the complete interrupted response remains visual.");
            }
            else { PresentResponse(AssistantState.Information, "Kora playback volume.", result, refreshOutput: false); }
        }
        catch (ArgumentOutOfRangeException exception)
        {
            if (!disposed)
            {
                if (preserveResponse || IsResponseInteractionPending)
                {
                    Transcript = "Use one exact playback percent from 0 through 100. " + exception.Message;
                    PreserveSpokenResponseFailure("The invalid volume request changed no preference; the complete original response remains visual.");
                }
                else { ShowFailure("Choose an exact playback percent from 0 through 100.", exception.Message); }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
            or OperationCanceledException or TimeoutException)
        {
            audioPlaybackSettings.HoldVolumeUnavailable();
            if (disposed) { return; }
            if (preserveResponse || IsResponseInteractionPending)
            {
                Transcript = "Volume preference not confirmed. " + exception.Message;
                PreserveSpokenResponseFailure("Volume preference was not confirmed; the complete interrupted response remains visual.");
            }
            else { ShowFailure("Volume preference not confirmed.", exception.Message); }
        }
        finally
        {
            volumeControlActive = false;
            SynchronizePlaybackVolume();
        }
    }
}
