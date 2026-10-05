namespace Kora.Application.Configuration;

internal interface IPreferenceStore
{
    string? ReadText(string fileName);

    string[]? ReadLines(string fileName);

    void WriteText(string fileName, string contents);

    void WriteLines(string fileName, IEnumerable<string> contents);

    void Delete(string fileName);
}