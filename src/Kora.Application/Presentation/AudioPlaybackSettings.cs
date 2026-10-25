using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Presentation;

/// <summary>Owns playback preference observation, selection and configuration subscriptions, not host admission or response output.</summary>
public sealed class AudioPlaybackSettings : IDisposable
{
    private readonly IUiDispatcher dispatcher;
    private readonly PlaybackVolumeConfigurationService? volume;
    private readonly WindowsSpeechRateConfigurationService? rate;
    private int selectedPlaybackVolume = PlaybackVolume.Default.Percent;
    private int selectedWindowsSpeechRate = WindowsSpeechRate.Default.Value;
    private bool disposed;

    public AudioPlaybackSettings(IUiDispatcher dispatcher, ILogger<AudioPlaybackSettings> logger,
        PlaybackVolumeConfigurationService? volume = null, WindowsSpeechRateConfigurationService? rate = null)
    {
        this.dispatcher = dispatcher;
        this.volume = volume;
        this.rate = rate;
        ObserveWindowsSpeechRate();
        selectedWindowsSpeechRate = rate?.Get().Desired?.Value ?? WindowsSpeechRate.Default.Value;
        if (volume is not null)
        {
            try { volume.Observe(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                volume.HoldUnavailable();
                ApplicationLog.Error(logger, exception, "Reading playback volume");
            }
            selectedPlaybackVolume = volume.Get().Desired?.Percent ?? PlaybackVolume.Default.Percent;
            volume.Changed += OnVolumeChanged;
        }
        if (rate is not null) { rate.Changed += OnRateChanged; }
    }

    // Invalidation stays synchronous; selected values and binding notifications belong to the UI dispatcher.
    public event EventHandler? OutputInvalidated;
    public event EventHandler<AudioPlaybackSetting>? Changed;

    public PlaybackVolumeState? VolumeState => volume?.Get();
    public WindowsSpeechRateState? RateState => rate?.Get();
    public bool IsVolumeControlActive { get; private set; }
    public bool IsRateControlActive { get; private set; }
    public IReadOnlyList<int> PlaybackVolumeChoices { get; } = Enumerable.Range(0, PlaybackVolume.MaximumPercent + 1).ToArray();
    public IReadOnlyList<int> WindowsSpeechRateChoices { get; } =
        Enumerable.Range(WindowsSpeechRate.Minimum, WindowsSpeechRate.Maximum - WindowsSpeechRate.Minimum + 1).ToArray();
    public int SelectedPlaybackVolume
    {
        get => selectedPlaybackVolume;
        set => selectedPlaybackVolume = new PlaybackVolume(value).Percent;
    }
    public int SelectedWindowsSpeechRate
    {
        get => selectedWindowsSpeechRate;
        set => selectedWindowsSpeechRate = new WindowsSpeechRate(value).Value;
    }
    public string PlaybackVolumeStatus => volume is null
        ? "Volume settings are unavailable. Playback volume is unchanged."
        : SettingsText.Describe(volume.Get());
    public string WindowsSpeechRateStatus => rate is null
        ? "Windows voice speed settings are unavailable. Other voices are unchanged."
        : SettingsText.Describe(rate.Get());

    public void ObserveWindowsSpeechRate()
    {
        if (disposed || rate is null) { return; }
        try { rate.Observe(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            rate.HoldUnavailable();
        }
    }

    public void HoldVolumeUnavailable() => volume?.HoldUnavailable();
    public void HoldRateUnavailable() => rate?.HoldUnavailable();

    public Task<PlaybackVolumeSuggestionReview> ReviewVolumeSuggestionAsync(CallCommunicationPolicy policy,
        TimeProvider time, ConversationMessage message, Func<bool> eligible,
        Func<CancellationToken, Task> validateSource, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (volume is null || IsVolumeControlActive)
        { throw new InvalidOperationException("Playback volume settings are unavailable or busy."); }
        return PlaybackVolumeSuggestionReview.ReviewAsync(volume, policy, time, message,
            () => !disposed && eligible(), validateSource, token);
    }

    public async Task<bool> ConfirmVolumeSuggestionAsync(PlaybackVolumeSuggestionReview review,
        SecurityAuditInitiator initiator, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (volume is null || IsVolumeControlActive || !review.BelongsTo(volume))
        { throw new InvalidOperationException("Playback volume settings are unavailable or busy."); }
        IsVolumeControlActive = true;
        try { return await review.ConfirmAsync(token, initiator); }
        finally
        {
            IsVolumeControlActive = false;
            Synchronize(AudioPlaybackSetting.Volume);
        }
    }

    public async Task<(bool Saved, string Outcome, PlaybackVolume? PreviousDesired)> ExecuteVolumeAsync(
        PlaybackVolumeCommand command, RequestOrigin origin, SecurityAuditInitiator initiator,
        CallCommunicationPolicy policy, Func<bool> remainsEligible, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (volume is null || IsVolumeControlActive) { throw new InvalidOperationException("Playback volume settings are unavailable or busy."); }
        bool Eligible() => !disposed && remainsEligible();
        IsVolumeControlActive = true;
        try
        {
            await volume.RefreshAsync(origin, Eligible, token);
            var saved = false;
            var outcome = "observed";
            PlaybackVolume? previousDesired = null;
            if (command.Adjustment is { } adjustment)
            {
                var current = volume.Get();
                var proposal = volume.ProposeAdjustment(adjustment, current.Revision, policy.Current.Revision);
                previousDesired = current.Desired;
                if (proposal is null)
                {
                    outcome = adjustment == AudioPreferenceAdjustment.Increase ? "unchanged-upper-boundary" : "unchanged-lower-boundary";
                }
                else
                {
                    saved = await volume.ApplyAsync(proposal, origin, initiator, policy, Eligible, token);
                    outcome = !saved ? "denied" : proposal.Value!.Value.Percent == PlaybackVolume.MaximumPercent ? "saved-upper-boundary"
                        : proposal.Value.Value.Percent == 0 ? "saved-lower-boundary" : "saved";
                }
            }
            else if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var proposal = volume.Propose(command.Operation == AppearanceCommandOperation.Reset ? null
                    : command.Value ?? throw new InvalidOperationException("An exact percent is required."),
                    volume.Get().Revision, policy.Current.Revision);
                saved = await volume.ApplyAsync(proposal, origin, initiator, policy, Eligible, token);
                outcome = saved ? "saved" : "denied";
            }
            return (saved, outcome, previousDesired);
        }
        finally
        {
            IsVolumeControlActive = false;
            Synchronize(AudioPlaybackSetting.Volume);
        }
    }

    public async Task<(bool Saved, string Outcome, WindowsSpeechRate? PreviousDesired)> ExecuteRateAsync(
        WindowsSpeechRateCommand command, RequestOrigin origin, SecurityAuditInitiator initiator,
        CallCommunicationPolicy policy, Func<bool> remainsEligible, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (rate is null || IsRateControlActive) { throw new InvalidOperationException("Windows voice speed settings are unavailable or busy."); }
        bool Eligible() => !disposed && remainsEligible();
        IsRateControlActive = true;
        try
        {
            await rate.RefreshAsync(origin, Eligible, token);
            var saved = false;
            var outcome = "observed";
            WindowsSpeechRate? previousDesired = null;
            if (command.Adjustment is { } adjustment)
            {
                var current = rate.Get();
                var proposal = rate.ProposeAdjustment(adjustment, current.Revision, policy.Current.Revision);
                previousDesired = current.Desired;
                if (proposal is null)
                {
                    outcome = adjustment == AudioPreferenceAdjustment.Increase ? "unchanged-upper-boundary" : "unchanged-lower-boundary";
                }
                else
                {
                    saved = await rate.ApplyAsync(proposal, origin, initiator, policy, Eligible, token);
                    outcome = !saved ? "denied" : proposal.Value!.Value.Value == WindowsSpeechRate.Maximum ? "saved-upper-boundary"
                        : proposal.Value.Value.Value == WindowsSpeechRate.Minimum ? "saved-lower-boundary" : "saved";
                }
            }
            else if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var proposal = rate.Propose(command.Operation == AppearanceCommandOperation.Reset ? null
                    : command.Value ?? throw new InvalidOperationException("An exact Windows-native rate is required."),
                    rate.Get().Revision, policy.Current.Revision);
                saved = await rate.ApplyAsync(proposal, origin, initiator, policy, Eligible, token);
                outcome = saved ? "saved" : "denied";
            }
            return (saved, outcome, previousDesired);
        }
        finally
        {
            IsRateControlActive = false;
            Synchronize(AudioPlaybackSetting.WindowsSpeechRate);
        }
    }

    private void OnVolumeChanged(object? sender, EventArgs args) => ConfigurationChanged(AudioPlaybackSetting.Volume);
    private void OnRateChanged(object? sender, EventArgs args) => ConfigurationChanged(AudioPlaybackSetting.WindowsSpeechRate);

    private void ConfigurationChanged(AudioPlaybackSetting setting)
    {
        if (disposed) { return; }
        OutputInvalidated?.Invoke(this, EventArgs.Empty);
        dispatcher.Post(() => Synchronize(setting));
    }

    private void Synchronize(AudioPlaybackSetting setting)
    {
        if (disposed) { return; }
        if (setting == AudioPlaybackSetting.Volume)
        {
            if (volume!.Get().Desired is { } desired) { SelectedPlaybackVolume = desired.Percent; }
        }
        else if (rate!.Get().Desired is { } desired) { SelectedWindowsSpeechRate = desired.Value; }
        Changed?.Invoke(this, setting);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        if (volume is not null) { volume.Changed -= OnVolumeChanged; }
        if (rate is not null) { rate.Changed -= OnRateChanged; }
    }
}
