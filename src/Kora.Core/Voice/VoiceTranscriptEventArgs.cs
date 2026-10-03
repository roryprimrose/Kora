namespace Kora.Core.Voice;

public sealed class VoiceTranscriptEventArgs(string transcript, float confidence) : EventArgs
{
    public string Transcript { get; } = transcript;

    public float Confidence { get; } = confidence;
}