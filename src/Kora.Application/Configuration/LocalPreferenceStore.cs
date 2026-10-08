using System.Text;

using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

internal sealed class LocalPreferenceStore(IApplicationDataPaths paths) : IPreferenceStore
{
    private readonly string directory = Path.Combine(paths.LocalRoot, "Preferences");
    private readonly Lock writeGate = new();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public string? ReadText(string fileName)
    {
        var path = GetPath(fileName);
        try
        {
            if (!File.Exists(path)) { return null; }
            var bytes = File.ReadAllBytes(path);
            var prefix = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
            return StrictUtf8.GetString(bytes.AsSpan(prefix));
        }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Saved preference is not valid UTF-8.", exception); }
    }

    public string[]? ReadLines(string fileName)
    {
        var text = ReadText(fileName);
        if (text is null) { return null; }
        using var reader = new StringReader(text);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) { lines.Add(line); }
        return lines.ToArray();
    }

    public void WriteText(string fileName, string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        WriteAtomically(fileName, temporaryPath =>
            File.WriteAllText(temporaryPath, contents, new UTF8Encoding(false)));
    }

    public void WriteLines(string fileName, IEnumerable<string> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        WriteAtomically(fileName, temporaryPath =>
            File.WriteAllLines(temporaryPath, contents, new UTF8Encoding(false)));
    }

    public void Delete(string fileName)
    {
        var path = GetPath(fileName);
        lock (writeGate)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private void WriteAtomically(string fileName, Action<string> write)
    {
        var path = GetPath(fileName);
        lock (writeGate)
        {
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(
                directory,
                $".{fileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                write(temporaryPath);
                using (var durable = new FileStream(temporaryPath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    durable.Flush(flushToDisk: true);
                }
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }

    private string GetPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.Contains('/')
            || fileName.Contains('\\')
            || fileName.Contains(':')
            || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            || fileName is "." or "..")
        {
            throw new ArgumentException(
                "Preference file names cannot contain a directory path.",
                nameof(fileName));
        }

        return Path.Combine(directory, fileName);
    }
}