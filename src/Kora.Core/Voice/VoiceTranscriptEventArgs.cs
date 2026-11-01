namespace Kora.Core.Voice;

public sealed class VoiceTranscriptEventArgs(
    string transcript,
    float confidence,
    long generation = 0,
    IVoiceActivationEvidence? activationEvidence = null) : EventArgs
{
    public string Transcript { get; } = transcript;

    public float Confidence { get; } = confidence;

    public long Generation { get; } = generation;

    /// <summary>Gets volatile evidence for host validation, never serialized or retained as authority.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IVoiceActivationEvidence? ActivationEvidence { get; } = activationEvidence;
}