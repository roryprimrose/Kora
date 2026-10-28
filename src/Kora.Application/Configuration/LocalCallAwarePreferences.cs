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
            "v2,0,0,0" => new(false, false, false),
            "v2,0,0,1" => new(false, false, true),
            "v2,0,1,0" => new(false, true, false),
            "v2,0,1,1" => new(false, true, true),
            "v2,1,0,0" => new(true, false, false),
            "v2,1,0,1" => new(true, false, true),
            "v2,1,1,0" => new(true, true, false),
            "v2,1,1,1" => new(true, true, true),
            _ => throw new InvalidDataException("The saved call-aware settings are invalid."),
        };
        ApplicationLog.Debug(logger, "Loaded call-aware settings");
        return settings;
    }

    public void Save(CallAwareSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var value = $"v2,{(settings.ShowVisualTextDuringCalls ? 1 : 0)},{(settings.AllowVoiceActivationDuringCalls ? 1 : 0)},{(settings.AllowSpokenApprovalsDuringCalls ? 1 : 0)}";
        store.WriteText(FileName, value);
        ApplicationLog.Information(logger, "Saved call-aware settings");
    }
}
