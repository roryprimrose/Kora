using Kora.Application.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalVoiceConsentPreferences(
    IApplicationDataPaths paths,
    ILogger<LocalVoiceConsentPreferences> logger) : IVoiceConsentPreferences
{
    private readonly string directory = Path.Combine(paths.LocalRoot, "Preferences");

    public bool? Load()
    {
        var path = Path.Combine(directory, "voice-consent.txt");
        if (!File.Exists(path))
        {
            return null;
        }

        var value = File.ReadAllText(path).Trim();
        ApplicationLog.Information(logger, "Loaded the device-local ongoing voice consent preference");
        return value switch
        {
            "granted-v1" => true,
            "declined-v1" => false,
            _ => throw new InvalidDataException("The saved ongoing voice consent is invalid. Review voice consent in Settings."),
        };
    }

    public void Save(bool consent)
    {
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, "voice-consent.tmp");
        File.WriteAllText(temporaryPath, consent ? "granted-v1" : "declined-v1");
        File.Move(temporaryPath, Path.Combine(directory, "voice-consent.txt"), overwrite: true);
        ApplicationLog.Information(logger, "Saved the explicit ongoing voice consent preference");
    }
}
