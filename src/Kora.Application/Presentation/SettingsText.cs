using Kora.Application.Configuration;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Voice;

namespace Kora.Application.Presentation;

internal static class SettingsText
{
    public static string ResponseMode(ResponseOutputMode mode) => mode switch
    {
        ResponseOutputMode.VisualOnly => "Text only",
        ResponseOutputMode.VoiceOnly => "Speech only",
        ResponseOutputMode.Hybrid => "Text and speech",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static string ProviderMode(ModelProviderMode mode) => mode switch
    {
        ModelProviderMode.Unknown => "Unavailable",
        ModelProviderMode.LocalOnly => "On-device only",
        ModelProviderMode.LocalFirst => "Prefer on-device",
        ModelProviderMode.HostedPreferred => "Prefer online",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static string CallFeedback(InCallFeedbackMode mode) => mode switch
    {
        InCallFeedbackMode.UI => "Text only",
        InCallFeedbackMode.Voice => "Speech only",
        InCallFeedbackMode.Both => "Text and speech",
        InCallFeedbackMode.Inherit => "Use normal response settings",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static string Service(ProviderService service) => service switch
    {
        ProviderService.Unset => "Not selected",
        ProviderService.Local => "On-device (Ollama)",
        ProviderService.Copilot => "GitHub Copilot",
        ProviderService.Claude => "Claude",
        ProviderService.Codex => "Codex",
        _ => throw new ArgumentOutOfRangeException(nameof(service)),
    };

    public static string CaptionPlacement(SpeechCaptionPlacement placement) => placement switch
    {
        SpeechCaptionPlacement.BottomRight => "Bottom right",
        SpeechCaptionPlacement.BottomLeft => "Bottom left",
        SpeechCaptionPlacement.TopRight => "Top right",
        SpeechCaptionPlacement.TopLeft => "Top left",
        _ => throw new ArgumentOutOfRangeException(nameof(placement)),
    };

    public static string Describe(DiagnosticRetentionState state) => state.Effective is { } days
        ? $"{days.Days} days for new diagnostic records. {Source(state.Source)}"
        : Unavailable(state.Recovery);

    public static string Describe(AuditRetentionState state) => state.Effective is { } days
        ? $"{days.Days} days for new security records. {Source(state.Source)}"
        : Unavailable(state.Recovery);

    public static string Describe(PlaybackVolumeState state) => state.Effective is { } volume
        ? $"{volume.Percent}%. {Source(state.Source)}" + (volume.AllowsSpeech ? string.Empty : " Speech is off; responses appear on screen.")
        : Unavailable(state.Recovery);

    public static string Describe(WindowsSpeechRateState state) => state.Effective is { } rate
        ? $"Windows voice speed: {rate.Value}. {Source(state.Source)}"
        : Unavailable(state.Recovery);

    public static string Describe(SessionQueueConfigurationState state) => state.Effective is { } limits
        ? $"Up to {limits.PendingPerSession} waiting tasks per session and {limits.ExecutionSlots} running at once. "
            + $"Waiting time limit: {limits.PendingLifetimeMinutes} minutes. Running time limit: {limits.ActiveBudgetMinutes} minutes. "
            + Source(state.Source)
        : Unavailable(state.Recovery);

    public static string Describe(OutputDeviceCommandResult state) =>
        (state.Available ? "Speaker ready. " : state.Muted
            ? "The speaker is muted; responses appear on screen. " : "Speaker unavailable. ")
        + Source(state.Source)
        + (state.Recovery is { } recovery ? " " + recovery : string.Empty);

    public static string Describe(ResponseModeCommandResult state) => state.Available && state.Desired is { } mode
        ? $"{ResponseMode(mode)}. {Source(state.Source)}"
        : Unavailable(state.Recovery);

    public static string Describe(ProviderModeCommandResult state) => state.Available && state.Desired is { } mode
        ? $"{ProviderMode(mode)} for new sessions. {Source(state.Source)}"
        : Unavailable(state.Recovery);

    public static string Describe(InCallFeedbackCommandResult state) => state.Available && state.Desired is { } mode
        ? $"{CallFeedback(mode)}. {Source(state.Source)}"
            + (state.Applied ? " Applies to the current call." : " Not currently applied.")
            + (state.MandatoryVisual ? " The full response must remain on screen." : string.Empty)
        : Unavailable(state.Recovery);

    public static string Describe(SpeechTextState state) => state.Available && state.Effective is { } mode
        ? (mode == SpeechTextMode.Off ? "Captions are off. " : "Show the words Kora speaks. ") + Source(state.Source)
        : Unavailable(state.Recovery);

    public static string Describe(ProviderProfileConfigurationState state)
    {
        if (!state.ConfigurationAvailable) { return Unavailable(state.Recovery); }
        var service = state.Desired?.Service ?? ProviderService.Unset;
        return $"Default service: {Service(service)}. "
            + (state.Effective is { } profile
                ? Describe(profile)
                : "No model is ready to use with these settings.")
            + " " + Source(state.Saved is null ? "default" : "saved");
    }

    public static string Describe(ProviderProfileTuple profile) =>
        $"Account: {profile.Account.Value}; model: {profile.Model.Value}; thinking level: {profile.Reasoning.Value}; conversation size: {profile.Context.Value}.";

    public static string Describe(SpeechConfigurationState state) =>
        (state.IsAvailable ? $"Voice: {state.EffectiveVoice!.Name}. " + Source(state.IsSaved ? "saved" : "default")
            : Unavailable(state.Recovery))
        + (state.SummaryLimits is { } limits
            ? $" Spoken summary limits: {limits.Sentences} sentences and {limits.Words} words."
            : " Spoken summary limits are unavailable. " + state.SummaryLimitsRecovery);

    public static string Describe(AppearanceOptionState state) => state.IsSaved
        ? "Appearance setting saved on this device."
        : "No saved preference for this appearance setting.";

    private static string Source(string source) => source switch
    {
        "default" => "Using the default.",
        "saved" => "Saved on this device.",
        _ => "Current setting on this device.",
    };

    private static string Unavailable(string? recovery) =>
        "Setting unavailable."
        + (string.IsNullOrWhiteSpace(recovery) ? " Refresh to check again." : " " + recovery);
}
