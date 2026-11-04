namespace Kora.Application.Voice;

// Offsets in the already-admitted caption slice, never a renderer text match or user selection.
public sealed record SpeechCaptionEmphasis(int Start, int Length);
