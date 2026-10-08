using Kora.Application.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalResponseOutputPreferences : IResponseOutputPreferences
{
    private const string DefaultModeFileName = "response-output-mode.txt";
    private const string UnconfirmedModeFileName = "response-output-mode-unconfirmed.txt";
    private const string MutedOutputFallbackFileName =
        "muted-output-visual-fallback.txt";
    private readonly IPreferenceStore store;
    private readonly ILogger<LocalResponseOutputPreferences> logger;

    public LocalResponseOutputPreferences(
        IApplicationDataPaths paths,
        ILogger<LocalResponseOutputPreferences> logger)
        : this(new LocalPreferenceStore(paths), logger)
    {
    }

    internal LocalResponseOutputPreferences(
        IPreferenceStore store,
        ILogger<LocalResponseOutputPreferences> logger)
    {
        this.store = store;
        this.logger = logger;
    }

    public ResponseOutputMode? LoadDefaultMode()
    {
        if (store.ReadText(UnconfirmedModeFileName) is not null)
        {
            throw new InvalidDataException("A response-mode write has unconfirmed evidence. Inspect the saved mode and audit receipts before explicit repair and refresh.");
        }
        return ReadBackDefaultMode();
    }

    public void BeginDefaultModeWrite() => store.WriteText(UnconfirmedModeFileName, "1");

    public void ConfirmDefaultModeWrite() => store.Delete(UnconfirmedModeFileName);

    public ResponseOutputMode? ReadBackDefaultMode()
    {
        var contents = store.ReadText(DefaultModeFileName);
        if (contents is null)
        {
            return null;
        }

        var value = contents.Trim();
        var result = Enum.TryParse<ResponseOutputMode>(
                value,
                ignoreCase: true,
                out var mode)
            && Enum.IsDefined(mode)
            ? mode
            : throw new InvalidDataException(
                $"The saved response output mode '{value}' is invalid.");
        ApplicationLog.Debug(logger, "Loaded the default response output mode");
        return result;
    }

    public void SaveDefaultMode(ResponseOutputMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "The response output mode is invalid.");
        }

        store.WriteText(DefaultModeFileName, mode.ToString());
        ApplicationLog.Information(logger, "Saved the default response output mode");
    }

    public bool? LoadMutedOutputVisualFallback()
    {
        var contents = store.ReadText(MutedOutputFallbackFileName);
        if (contents is null)
        {
            return null;
        }

        var enabled = contents.Trim() switch
        {
            "0" => false,
            "1" => true,
            var value => throw new InvalidDataException(
                $"The saved muted-output visual fallback '{value}' is invalid."),
        };
        ApplicationLog.Debug(logger, "Loaded the muted-output visual fallback");
        return enabled;
    }

    public void SaveMutedOutputVisualFallback(bool enabled)
    {
        store.WriteText(MutedOutputFallbackFileName, enabled ? "1" : "0");
        ApplicationLog.Information(logger, "Saved the muted-output visual fallback");
    }
}
