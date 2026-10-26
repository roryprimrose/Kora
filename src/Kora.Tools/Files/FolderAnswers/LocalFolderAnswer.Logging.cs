using Microsoft.Extensions.Logging;

namespace Kora.Tools.Files.FolderAnswers;

public sealed partial class LocalFolderAnswer
{
    [LoggerMessage(1, LogLevel.Warning, "Local folder answer unavailable; failure type {FailureType}")]
    private static partial void Failure(ILogger logger, string failureType);
}
