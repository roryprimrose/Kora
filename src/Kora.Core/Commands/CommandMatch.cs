namespace Kora.Core.Commands;

public sealed record CommandMatch(
    bool IsMatch,
    string Transcript,
    string NormalizedTranscript,
    CommandDefinition? Command)
{
    public static CommandMatch NotMatched(string transcript, string normalizedTranscript) =>
        new(false, transcript, normalizedTranscript, null);
}