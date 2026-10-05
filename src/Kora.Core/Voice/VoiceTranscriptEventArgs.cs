namespace Kora.Core.Voice;

public sealed class VoiceTranscriptEventArgs(
    string transcript,
    float confidence,
    long generation = 0) : EventArgs
{
    public string Transcript { get; } = transcript;

    public float Confidence { get; } = confidence;

    public long Generation { get; } = generation;
}