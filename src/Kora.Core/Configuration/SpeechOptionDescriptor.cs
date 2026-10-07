namespace Kora.Core.Configuration;

public sealed record SpeechOptionDescriptor(SpeechOption Option, string Id, string SpokenName,
    string Default, string ResetEffect)
{
    public string Type => "installed-choice";
    public string Scope => "device-local";
    public string Effect => "voice-output";
    public string Availability => "ready-installed-assets";
    public string ApplicationTiming => "after-atomic-save; next speech operation";
    public string Confirmation => "exact local choice; protected-call original-channel gate";
    public string AuditAction => Option == SpeechOption.Provider
        ? "configuration.speech-provider" : "configuration.voice-selection";
}
