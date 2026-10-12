using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kora.ManualProofHarness;

internal sealed class ResultStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string path;

    internal ResultStore(string path)
    {
        this.path = System.IO.Path.GetFullPath(path);
    }

    internal string Path => path;

    internal ProofResults Load()
    {
        if (!File.Exists(path))
        {
            return new();
        }

        try
        {
            using var stream = File.OpenRead(path);
            var results = JsonSerializer.Deserialize<ProofResults>(stream, JsonOptions)
                ?? throw new InvalidDataException("The result file contains no document.");
            Validate(results);
            return results;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The result file is malformed: {path}", exception);
        }
    }

    internal void Save(ProofResults results)
    {
        Validate(results);
        var directory = System.IO.Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("The result path must have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = System.IO.Path.Combine(directory, $".{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, results, JsonOptions);
                stream.Flush(flushToDisk: true);
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

    private static void Validate(ProofResults results)
    {
        if (results.FormatVersion != 1)
        {
            throw new InvalidDataException($"Unsupported result format version {results.FormatVersion}.");
        }

        foreach (var attempt in results.Attempts)
        {
            if (attempt.AttemptId == Guid.Empty || string.IsNullOrWhiteSpace(attempt.ProofId))
            {
                throw new InvalidDataException("Every attempt requires an ID and proof ID.");
            }

            if (attempt.NextStepIndex < 0)
            {
                throw new InvalidDataException("An attempt contains an invalid next-step index.");
            }
        }
    }
}
