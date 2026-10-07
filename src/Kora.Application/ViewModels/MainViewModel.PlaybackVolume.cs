using Kora.Application.Configuration;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly PlaybackVolumeConfigurationService? playbackVolumeConfiguration;
    private int selectedPlaybackVolume = PlaybackVolume.Default.Percent;
    private bool volumeControlActive;

    public IReadOnlyList<int> PlaybackVolumeChoices { get; } = Enumerable.Range(0, PlaybackVolume.MaximumPercent + 1).ToArray();
    public int SelectedPlaybackVolume
    {
        get => selectedPlaybackVolume;
        set => SetProperty(ref selectedPlaybackVolume, new PlaybackVolume(value).Percent);
    }
    public AsyncCommand RefreshPlaybackVolumeCommand { get; }
    public AsyncCommand SavePlaybackVolumeCommand { get; }
    public AsyncCommand ResetPlaybackVolumeCommand { get; }
    public bool CanChangePlaybackVolume => playbackVolumeConfiguration is not null && IsCallMutationHostEligible
        && !volumeControlActive && !IsResponseInteractionPending;
    public string PlaybackVolumeStatus => playbackVolumeConfiguration is null
        ? "Playback volume admission is unavailable; existing unscaled output is retained."
        : PlaybackVolumeState.Serialize(playbackVolumeConfiguration.Get(), CallPolicyRevision, "observed");

    private void OnPlaybackVolumeChanged(object? sender, EventArgs args)
    {
        if (IsSpeaking) { forceVisualResponse = true; }
        uiDispatcher.Post(SynchronizePlaybackVolume);
    }

    private void SynchronizePlaybackVolume()
    {
        if (disposed) { return; }
        if (playbackVolumeConfiguration!.Get().Desired is { } volume) { SelectedPlaybackVolume = volume.Percent; }
        OnPropertyChanged(nameof(PlaybackVolumeStatus));
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
        var eligible = CaptureAudioControlEligibility(origin);
        var preserveResponse = IsSpeaking;
        volumeControlActive = true;
        OnPropertyChanged(nameof(CanChangePlaybackVolume));
        try
        {
            await playbackVolumeConfiguration!.RefreshAsync(origin, eligible, cancellationToken);
            var saved = false;
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var proposal = playbackVolumeConfiguration.Propose(command.Operation == AppearanceCommandOperation.Reset ? null
                    : command.Value ?? throw new InvalidOperationException("An exact percent is required."),
                    playbackVolumeConfiguration.Get().Revision, CallPolicyRevision);
                saved = await playbackVolumeConfiguration.ApplyAsync(proposal, origin, initiator, communicationPolicy, eligible, cancellationToken);
            }
            if (!eligible()) { return; }
            if (saved && IsSpeaking) { await textToSpeech.StopAsync(CancellationToken.None); }
            if (!eligible()) { return; }
            var result = PlaybackVolumeState.Serialize(playbackVolumeConfiguration.Get(), CallPolicyRevision,
                command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset ? saved ? "saved" : "denied" : "observed");
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
            playbackVolumeConfiguration!.HoldUnavailable();
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
