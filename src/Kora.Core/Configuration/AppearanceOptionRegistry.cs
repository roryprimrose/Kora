namespace Kora.Core.Configuration;

public static class AppearanceOptionRegistry
{
    public static IReadOnlyList<AppearanceOptionDescriptor> Options { get; } =
        Array.AsReadOnly<AppearanceOptionDescriptor>(
        [
            new(AppearanceOption.Theme, "appearance.theme", "Appearance theme",
                AppearanceValueType.Theme, "mode", new AppearanceValue.Theme(ApplicationThemeSettings.DefaultMode),
                null, null, "configuration.appearance-theme"),
            new(AppearanceOption.PresenceDisplay, "appearance.presence-display", "Presence display",
                AppearanceValueType.Toggle, "boolean", new AppearanceValue.Toggle(PresenceSettings.DefaultDisplayEnabled),
                null, null, "configuration.presence-display"),
            new(AppearanceOption.PresenceTimeout, "appearance.presence-timeout", "Presence timeout",
                AppearanceValueType.Number, "seconds", new AppearanceValue.Number(PresenceSettings.DefaultTimeoutSeconds),
                PresenceSettings.MinimumTimeoutSeconds, PresenceSettings.MaximumTimeoutSeconds, "configuration.presence-timeout"),
            new(AppearanceOption.ResponseTimeout, "appearance.response-timeout", "Response timeout",
                AppearanceValueType.Number, "seconds", new AppearanceValue.Number(ResponseWindowSettings.DefaultTimeoutSeconds),
                ResponseWindowSettings.MinimumTimeoutSeconds, ResponseWindowSettings.MaximumTimeoutSeconds, "configuration.response-timeout"),
            new(AppearanceOption.PresenceSize, "appearance.presence-size", "Presence size",
                AppearanceValueType.Number, "pixels", new AppearanceValue.Number(PresenceSettings.DefaultSizePixels),
                PresenceSettings.MinimumSizePixels, PresenceSettings.MaximumSizePixels, "configuration.presence-size"),
            new(AppearanceOption.DotSize, "appearance.dot-size", "Presence dot size",
                AppearanceValueType.Number, "percent", new AppearanceValue.Number(PresenceSettings.DefaultDotSizePercent),
                PresenceSettings.MinimumDotSizePercent, PresenceSettings.MaximumDotSizePercent, "configuration.presence-dot-size"),
            new(AppearanceOption.DotDensity, "appearance.dot-density", "Presence dot density",
                AppearanceValueType.Number, "percent", new AppearanceValue.Number(PresenceSettings.DefaultDotDensityPercent),
                PresenceSettings.MinimumDotDensityPercent, PresenceSettings.MaximumDotDensityPercent, "configuration.presence-dot-density"),
            new(AppearanceOption.MovementSpeed, "appearance.movement-speed", "Presence movement speed",
                AppearanceValueType.Number, "percent", new AppearanceValue.Number(PresenceSettings.DefaultMovementSpeedPercent),
                PresenceSettings.MinimumMovementSpeedPercent, PresenceSettings.MaximumMovementSpeedPercent, "configuration.presence-movement-speed"),
            new(AppearanceOption.SpeechScaling, "appearance.speech-scaling", "Presence speech scaling",
                AppearanceValueType.Toggle, "boolean", new AppearanceValue.Toggle(PresenceSettings.DefaultSpeechScalingEnabled),
                null, null, "configuration.presence-speech-scaling"),
            new(AppearanceOption.SpeechScaleAmount, "appearance.speech-scale-amount", "Presence speech scale amount",
                AppearanceValueType.Number, "percent", new AppearanceValue.Number(PresenceSettings.DefaultSpeechScaleAmountPercent),
                PresenceSettings.MinimumSpeechScaleAmountPercent, PresenceSettings.MaximumSpeechScaleAmountPercent, "configuration.presence-speech-scale-amount"),
        ]);

    public static AppearanceOptionDescriptor Get(AppearanceOption option) =>
        Options.FirstOrDefault(item => item.Option == option)
        ?? throw new ArgumentOutOfRangeException(nameof(option));

    public static void Validate(AppearanceOption option, AppearanceValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var descriptor = Get(option);
        switch (option, value)
        {
            case (AppearanceOption.Theme, AppearanceValue.Theme theme):
                ApplicationThemeSettings.Validate(theme.Value);
                return;
            case (AppearanceOption.PresenceDisplay, AppearanceValue.Toggle):
            case (AppearanceOption.SpeechScaling, AppearanceValue.Toggle):
                return;
            case (AppearanceOption.PresenceTimeout, AppearanceValue.Number integer):
                PresenceSettings.ValidateTimeoutSeconds(integer.Value);
                return;
            case (AppearanceOption.ResponseTimeout, AppearanceValue.Number integer):
                ResponseWindowSettings.ValidateTimeoutSeconds(integer.Value);
                return;
            case (AppearanceOption.PresenceSize, AppearanceValue.Number integer):
                PresenceSettings.ValidateSizePixels(integer.Value);
                return;
            case (AppearanceOption.DotSize, AppearanceValue.Number integer):
                PresenceSettings.ValidateDotSizePercent(integer.Value);
                return;
            case (AppearanceOption.DotDensity, AppearanceValue.Number integer):
                PresenceSettings.ValidateDotDensityPercent(integer.Value);
                return;
            case (AppearanceOption.MovementSpeed, AppearanceValue.Number integer):
                PresenceSettings.ValidateMovementSpeedPercent(integer.Value);
                return;
            case (AppearanceOption.SpeechScaleAmount, AppearanceValue.Number integer):
                PresenceSettings.ValidateSpeechScaleAmountPercent(integer.Value);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), $"The value must have the admitted {descriptor.Type} type.");
        }
    }
}
