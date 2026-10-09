namespace Kora.Core.Configuration;

public sealed record SpeechOptionDescriptor(SpeechOption Option, string Id, string SpokenName,
    string Default, string ResetEffect)
{
    public bool IsSummaryLimit => Option is SpeechOption.SummarySentences or SpeechOption.SummaryWords;
    public string Type => IsSummaryLimit ? "positive-integer" : "installed-choice";
    public string Scope => "device-local";
    public string Effect => "voice-output";
    public string Availability => IsSummaryLimit ? "local-host" : "ready-installed-assets";
    public string ApplicationTiming => "after-atomic-save; next speech operation";
    public string Confirmation => "exact local choice; protected-call original-channel gate";
    public string AuditAction => Option switch
    {
        SpeechOption.Provider => "configuration.speech-provider",
        SpeechOption.Voice => "configuration.voice-selection",
        SpeechOption.SummarySentences => "configuration.speech-summary-sentences",
        SpeechOption.SummaryWords => "configuration.speech-summary-words",
        _ => throw new InvalidOperationException("Unknown speech option."),
    };
}
