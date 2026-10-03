using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Application.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalCallAwarePreferences(
    IApplicationDataPaths paths,
    ILogger<LocalCallAwarePreferences> logger) : ICallAwarePreferences
{
    private readonly string preferenceDirectory = Path.Combine(paths.LocalRoot, "Preferences");
    private readonly string preferencePath = Path.Combine(paths.LocalRoot, "Preferences", "call-aware-settings.txt");

    public CallAwareSettings? Load()
    {
        if (!File.Exists(preferencePath))
        {
            return null;
        }

        var settings = File.ReadAllText(preferencePath).Trim() switch
        {
            "0,0" => new CallAwareSettings(false, false),
            "0,1" => new CallAwareSettings(false, true),
            "1,0" => new CallAwareSettings(true, false),
            "1,1" => new CallAwareSettings(true, true),
            var value => throw new InvalidDataException($"The saved call-aware settings '{value}' are invalid."),
        };
        ApplicationLog.Debug(logger, "Loaded call-aware settings");
        return settings;
    }

    public void Save(CallAwareSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(preferenceDirectory);
        var temporaryPath = Path.Combine(preferenceDirectory, "call-aware-settings.tmp");
        var value = $"{(settings.ShowVisualTextDuringCalls ? 1 : 0)},{(settings.AllowVoiceActivationDuringCalls ? 1 : 0)}";
        File.WriteAllText(temporaryPath, value);
        File.Move(temporaryPath, preferencePath, overwrite: true);
        ApplicationLog.Information(logger, "Saved call-aware settings");
    }
}
