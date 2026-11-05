namespace Kora.Core.Configuration.RegisteredSettings;

public sealed record RegisteredSettingDescriptor(
    RegisteredSetting Setting, string Id, string Description, string Units,
    int? Minimum, int? Maximum, bool RequiresNativeReview)
{
    public string Scope => "shared device-local";
    public string SuggestedValues => Setting switch
    {
        RegisteredSetting.ResponseMode => "Hybrid/VoiceOnly/VisualOnly",
        RegisteredSetting.QuietHours => "Off or weekly <ordered days> <HH:mm start> <HH:mm end> <explicit installed zone ID>",
        RegisteredSetting.SpeechVoice => "exact installed provider / voice ID supplied by the user",
        RegisteredSetting.PresenceDisplay or RegisteredSetting.SpeechScaling or RegisteredSetting.ReducedMotion => "true/false",
        RegisteredSetting.CommandStartWait => "2-10 seconds, exact decimal (at most seven fractional digits)",
        RegisteredSetting.ReplyStartWait => "5-60 seconds, exact decimal (at most seven fractional digits)",
        RegisteredSetting.TrailingSilence => "0.5-3 seconds, exact decimal (at most seven fractional digits)",
        _ => Minimum is { } minimum ? $"{minimum}-{Maximum} {Units}" : "system/light/dark",
    };
}
