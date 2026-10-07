using System.Text;

using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Diagnostics;

public sealed class LocalApplicationLogReader(
    IApplicationDataPaths paths,
    ILogger<LocalApplicationLogReader> logger) : IApplicationLogReader
{
    public const int MaximumReadCharacters = 1_000_000;

    private readonly string logDirectory = Path.Combine(paths.LocalRoot, DailyLogFilePolicy.DirectoryName);

    public IReadOnlyList<ApplicationLogFile> GetAvailableLogs()
    {
        if (!Directory.Exists(logDirectory))
        {
            return [];
        }

        var logs = Directory
            .EnumerateFiles(logDirectory, DailyLogFilePolicy.Pattern, SearchOption.TopDirectoryOnly)
            .Select(path => TryCreateLogFile(path, out var logFile) ? logFile : null)
            .OfType<ApplicationLogFile>()
            .OrderByDescending(file => file.Date)
            .ToArray();
        ApplicationLog.LogsDiscovered(logger, logs.Length);
        return logs;
    }

    public async Task<string> ReadTailAsync(
        string fileName,
        int maxCharacters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (maxCharacters is <= 0 or > MaximumReadCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCharacters),
                maxCharacters,
                $"The maximum character count must be between 1 and {MaximumReadCharacters}.");
        }

        if (!DailyLogFilePolicy.TryParseName(fileName, out _))
        {
            throw new ArgumentException("The file name is not a Kora daily log file.", nameof(fileName));
        }

        var path = Path.Combine(logDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The requested Kora log file does not exist.", fileName);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var maximumBytes = checked(maxCharacters * 4L);
        if (stream.Length > maximumBytes)
        {
            stream.Seek(-maximumBytes, SeekOrigin.End);
        }

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var tail = content.Length <= maxCharacters
            ? content
            : content[^maxCharacters..];
        ApplicationLog.LogRead(
            logger,
            tail.Length,
            fileName);
        return tail;
    }

    private static bool TryCreateLogFile(string path, out ApplicationLogFile? logFile)
    {
        var fileName = Path.GetFileName(path);
        if (!DailyLogFilePolicy.TryParseName(fileName, out var date))
        {
            logFile = null;
            return false;
        }

        var file = new FileInfo(path);
        logFile = new ApplicationLogFile(
            fileName,
            date,
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc));
        return true;
    }

}
