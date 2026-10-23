namespace Kora.Core.Voice;

/// <summary>An immutable host-selected configuration; audio and final events retain their capture generation separately.</summary>
public sealed record SpeechInputGeneration(long Number, SpeechInputSelection Selection);
