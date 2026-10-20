using Kora.Application.Configuration;

namespace Kora.Application.UnitTests.Configuration;

internal sealed class MalformedAudioPreferenceStore(string[] lines) : IPreferenceStore
{
    public string? ReadText(string fileName) => null;
    public string[]? ReadLines(string fileName) => lines;
    public void WriteText(string fileName, string contents) => throw new InvalidOperationException("Corrupt preferences must not be written.");
    public void WriteLines(string fileName, IEnumerable<string> contents) => throw new InvalidOperationException("Corrupt preferences must not be written.");
    public void Delete(string fileName) => throw new InvalidOperationException("Corrupt preferences must not be reset.");
}
