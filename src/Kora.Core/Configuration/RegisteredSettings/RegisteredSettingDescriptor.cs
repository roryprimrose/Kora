namespace Kora.Core.Configuration.RegisteredSettings;

public sealed record RegisteredSettingDescriptor(
    RegisteredSetting Setting, string Id, string Description, string Units,
    int? Minimum, int? Maximum, bool RequiresNativeReview)
{
    public string Scope => "shared device-local";
    public string SuggestedValues => Setting == RegisteredSetting.ResponseMode ? "Hybrid/VoiceOnly/VisualOnly"
        : Minimum is { } minimum ? $"{minimum}-{Maximum} {Units}" : "system/light/dark";
}
