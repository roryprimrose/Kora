using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Application.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalCallAwarePreferences : ICallAwarePreferences
{
    private const string FileName = "call-aware-settings.txt";
    private readonly IPreferenceStore store;
    private readonly ILogger<LocalCallAwarePreferences> logger;

    public LocalCallAwarePreferences(
        IApplicationDataPaths paths,
        ILogger<LocalCallAwarePreferences> logger)
        : this(new LocalPreferenceStore(paths), logger)
    {
    }

    internal LocalCallAwarePreferences(
        IPreferenceStore store,
        ILogger<LocalCallAwarePreferences> logger)
    {
        this.store = store;
        this.logger = logger;
    }

    public CallAwareSettings? Load()
    {
        var contents = store.ReadText(FileName);
        if (contents is null)
        {
            return null;
        }

        var settings = contents.Trim() switch
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

        var value = $"{(settings.ShowVisualTextDuringCalls ? 1 : 0)},{(settings.AllowVoiceActivationDuringCalls ? 1 : 0)}";
        store.WriteText(FileName, value);
        ApplicationLog.Information(logger, "Saved call-aware settings");
    }
}
