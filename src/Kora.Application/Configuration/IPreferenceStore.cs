namespace Kora.Application.Configuration;

internal interface IPreferenceStore
{
    string? ReadText(string fileName);

    string? ReadText(string fileName, int maximumBytes)
    {
        var text = ReadText(fileName);
        if (text is not null && System.Text.Encoding.UTF8.GetByteCount(text) > maximumBytes)
        { throw new InvalidDataException("The saved preference exceeds its byte limit."); }
        return text;
    }

    string[]? ReadLines(string fileName);

    void WriteText(string fileName, string contents);

    void WriteLines(string fileName, IEnumerable<string> contents);

    void Delete(string fileName);
}