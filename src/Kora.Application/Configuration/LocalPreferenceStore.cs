using System.Text;

using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

internal sealed class LocalPreferenceStore(IApplicationDataPaths paths) : IPreferenceStore
{
    private readonly string directory = Path.Combine(paths.LocalRoot, "Preferences");
    private readonly Lock writeGate = new();

    public string? ReadText(string fileName)
    {
        var path = GetPath(fileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public string[]? ReadLines(string fileName)
    {
        var path = GetPath(fileName);
        return File.Exists(path) ? File.ReadAllLines(path) : null;
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