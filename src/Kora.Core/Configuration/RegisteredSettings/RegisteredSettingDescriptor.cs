namespace Kora.Core.Configuration.RegisteredSettings;

public sealed record RegisteredSettingDescriptor(
    RegisteredSetting Setting, string Id, string Description, string Units,
    int? Minimum, int? Maximum, bool RequiresNativeReview)
{
    public string Scope => "shared device-local";
    public string SuggestedValues => Setting switch
    {
        RegisteredSetting.ResponseMode => "Hybrid/VoiceOnly/VisualOnly",
        RegisteredSetting.PresenceDisplay or RegisteredSetting.SpeechScaling => "true/false",
        _ => Minimum is { } minimum ? $"{minimum}-{Maximum} {Units}" : "system/light/dark",
    };
}
