using Kora.Application.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalVoiceConsentPreferences : IVoiceConsentPreferences
{
    private const string FileName = "voice-consent.txt";
    private readonly IPreferenceStore store;
    private readonly ILogger<LocalVoiceConsentPreferences> logger;

    public LocalVoiceConsentPreferences(
        IApplicationDataPaths paths,
        ILogger<LocalVoiceConsentPreferences> logger)
        : this(new LocalPreferenceStore(paths), logger)
    {
    }

    internal LocalVoiceConsentPreferences(
        IPreferenceStore store,
        ILogger<LocalVoiceConsentPreferences> logger)
    {
        this.store = store;
        this.logger = logger;
    }

    public bool? Load()
    {
        var contents = store.ReadText(FileName);
        if (contents is null)
        {
            return null;
        }

        var value = contents.Trim();
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
        store.WriteText(FileName, consent ? "granted-v1" : "declined-v1");
        ApplicationLog.Information(logger, "Saved the explicit ongoing voice consent preference");
    }
}
