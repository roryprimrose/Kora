using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Application.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalResponseOutputPreferences(
    IApplicationDataPaths paths,
    ILogger<LocalResponseOutputPreferences> logger) : IResponseOutputPreferences
{
    private readonly string preferenceDirectory = Path.Combine(paths.LocalRoot, "Preferences");
    private readonly string preferencePath = Path.Combine(paths.LocalRoot, "Preferences", "response-output-mode.txt");

    public ResponseOutputMode? LoadDefaultMode()
    {
        if (!File.Exists(preferencePath))
        {
            return null;
        }

        var value = File.ReadAllText(preferencePath).Trim();
        var result = Enum.TryParse<ResponseOutputMode>(value, ignoreCase: true, out var mode)
               && Enum.IsDefined(mode)
            ? mode
            : throw new InvalidDataException($"The saved response output mode '{value}' is invalid.");
        ApplicationLog.Debug(logger, "Loaded the default response output mode");
        return result;
    }

    public void SaveDefaultMode(ResponseOutputMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The response output mode is invalid.");
        }

        Directory.CreateDirectory(preferenceDirectory);
        var temporaryPath = Path.Combine(preferenceDirectory, "response-output-mode.tmp");
        File.WriteAllText(temporaryPath, mode.ToString());
        File.Move(temporaryPath, preferencePath, overwrite: true);
        ApplicationLog.Information(logger, "Saved the default response output mode");
    }
}