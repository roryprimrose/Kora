using System.Globalization;

using Kora.Application.Diagnostics;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalAppearancePreferences : IAppearancePreferences
{
    private const string FileName = "appearance-theme.txt";
    private const string PresenceDisplayEnabledFileName = "presence-display-enabled.txt";
    private const string PresenceTimeoutFileName = "presence-inactivity-timeout-seconds.txt";
    private const string ResponseTimeoutFileName = "response-timeout-seconds.txt";
    private const string LegacyResponseTimeoutFileName = "presence-timeout-seconds.txt";
    private const string PresenceSizeFileName = "presence-size-pixels.txt";
    private const string PresenceDotSizeFileName = "presence-dot-size-percent.txt";
    private const string PresenceDotDensityFileName = "presence-dot-density-percent.txt";
    private const string PresenceMovementSpeedFileName = "presence-movement-speed-percent.txt";
    private const string PresenceSpeechScalingEnabledFileName = "presence-speech-scaling-enabled.txt";
    private const string PresenceSpeechScaleAmountFileName = "presence-speech-scale-amount-percent.txt";
    private const string PresencePositionFileName = "presence-position.txt";
    private const string ResponseWindowFileName = "response-window.txt";
    private readonly IPreferenceStore store;
    private readonly ILogger<LocalAppearancePreferences> logger;

    public LocalAppearancePreferences(
        IApplicationDataPaths paths,
        ILogger<LocalAppearancePreferences> logger)
        : this(new LocalPreferenceStore(paths), logger)
    {
    }

    internal LocalAppearancePreferences(
        IPreferenceStore store,
        ILogger<LocalAppearancePreferences> logger)
    {
        this.store = store;
        this.logger = logger;
    }

    public ApplicationThemeMode? LoadThemeMode()
    {
        var contents = store.ReadText(FileName);
        if (contents is null)
        {
            return null;
        }

        var value = contents.Trim();
        var result = Enum.TryParse<ApplicationThemeMode>(value, ignoreCase: true, out var mode)
            ? mode
            : throw new InvalidDataException("The saved appearance theme is invalid.");
        try
        {
            ApplicationThemeSettings.Validate(result);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The saved appearance theme is invalid.", exception);
        }
        ApplicationLog.Debug(logger, "Loaded the appearance theme preference");
        return result;
    }

    public void SaveThemeMode(ApplicationThemeMode mode)
    {
        ApplicationThemeSettings.Validate(mode);

        store.WriteText(FileName, mode.ToString());
        ApplicationLog.Information(logger, "Saved the appearance theme preference");
    }

    public bool? LoadPresenceDisplayEnabled()
    {
        var value = LoadBooleanPreference(
            PresenceDisplayEnabledFileName,
            "presence display");
        if (value is not null)
        {
            ApplicationLog.Debug(logger, "Loaded the presence display preference");
        }
        return value;
    }

    public void SavePresenceDisplayEnabled(bool value)
    {
        store.WriteText(
            PresenceDisplayEnabledFileName,
            value.ToString(CultureInfo.InvariantCulture));
        ApplicationLog.Information(logger, "Saved the presence display preference");
    }

    public int? LoadPresenceTimeoutSeconds() =>
        LoadIntegerPreference(
            PresenceTimeoutFileName,
            PresenceSettings.ValidateTimeoutSeconds,
            "presence timeout");

    public void SavePresenceTimeoutSeconds(int seconds)
    {
        PresenceSettings.ValidateTimeoutSeconds(seconds);

        SaveIntegerPreference(
            seconds,
            PresenceTimeoutFileName);
        ApplicationLog.Information(logger, "Saved the presence timeout preference");
    }

    public int? LoadResponseTimeoutSeconds() =>
        LoadIntegerPreference(
            ResponseTimeoutFileName,
            ResponseWindowSettings.ValidateTimeoutSeconds,
            "response timeout")
        ?? LoadIntegerPreference(
            LegacyResponseTimeoutFileName,
            ResponseWindowSettings.ValidateTimeoutSeconds,
            "legacy response timeout");

    public void SaveResponseTimeoutSeconds(int seconds)
    {
        ResponseWindowSettings.ValidateTimeoutSeconds(seconds);
        SaveIntegerPreference(seconds, ResponseTimeoutFileName);
        ApplicationLog.Information(logger, "Saved the response timeout preference");
    }

    public int? LoadPresenceSizePixels() =>
        LoadIntegerPreference(
            PresenceSizeFileName,
            PresenceSettings.ValidateSizePixels,
            "presence size");

    public int? LoadPresenceDotSizePercent() =>
        LoadIntegerPreference(
            PresenceDotSizeFileName,
            PresenceSettings.ValidateDotSizePercent,
            "presence dot size");

    public int? LoadPresenceDotDensityPercent() =>
        LoadIntegerPreference(
            PresenceDotDensityFileName,
            PresenceSettings.ValidateDotDensityPercent,
            "presence dot density");

    public int? LoadPresenceMovementSpeedPercent() =>
        LoadIntegerPreference(
            PresenceMovementSpeedFileName,
            PresenceSettings.ValidateMovementSpeedPercent,
            "presence movement speed");

    public bool? LoadPresenceSpeechScalingEnabled()
    {
        var value = LoadBooleanPreference(
            PresenceSpeechScalingEnabledFileName,
            "presence speech scaling");
        if (value is not null)
        {
            ApplicationLog.Debug(logger, "Loaded the presence speech scaling preference");
        }
        return value;
    }

    public int? LoadPresenceSpeechScaleAmountPercent() =>
        LoadIntegerPreference(
            PresenceSpeechScaleAmountFileName,
            PresenceSettings.ValidateSpeechScaleAmountPercent,
            "presence speech scale amount");

    public PresencePosition? LoadPresencePosition()
    {
        var contents = store.ReadText(PresencePositionFileName);
        if (contents is null)
        {
            return null;
        }

        var coordinates = contents
            .Split(',', StringSplitOptions.TrimEntries);
        if (coordinates.Length != 2
            || !int.TryParse(
                coordinates[0],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var x)
            || !int.TryParse(
                coordinates[1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var y))
        {
            throw new InvalidDataException("The saved presence position is invalid.");
        }

        ApplicationLog.Debug(logger, "Loaded the presence position preference");
        return new PresencePosition(x, y);
    }

    public ResponseWindowSettings? LoadResponseWindowSettings()
    {
        var values = store.ReadLines(ResponseWindowFileName);
        if (values is null)
        {
            return null;
        }

        if (values.Length != 3
            || !bool.TryParse(values[0], out var alwaysShow)
            || !bool.TryParse(values[1], out var topmost))
        {
            throw new InvalidDataException("The saved response window settings are invalid.");
        }

        ResponseWindowPosition? position = null;
        if (!string.IsNullOrWhiteSpace(values[2]))
        {
            var coordinates = values[2].Split(',', StringSplitOptions.TrimEntries);
            if (coordinates.Length != 2
                || !int.TryParse(
                    coordinates[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var x)
                || !int.TryParse(
                    coordinates[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var y))
            {
                throw new InvalidDataException("The saved response window position is invalid.");
            }

            position = new ResponseWindowPosition(x, y);
        }

        ApplicationLog.Debug(logger, "Loaded the response window preference");
        return new ResponseWindowSettings(alwaysShow, topmost, position);
    }

    public void SavePresenceSizePixels(int value)
    {
        PresenceSettings.ValidateSizePixels(value);
        SaveIntegerPreference(
            value,
            PresenceSizeFileName);
        ApplicationLog.Information(logger, "Saved the presence size preference");
    }

    public void SavePresenceDotSizePercent(int value)
    {
        PresenceSettings.ValidateDotSizePercent(value);
        SaveIntegerPreference(
            value,
            PresenceDotSizeFileName);
        ApplicationLog.Information(logger, "Saved the presence dot size preference");
    }

    public void SavePresenceDotDensityPercent(int value)
    {
        PresenceSettings.ValidateDotDensityPercent(value);
        SaveIntegerPreference(
            value,
            PresenceDotDensityFileName);
        ApplicationLog.Information(logger, "Saved the presence dot density preference");
    }

    public void SavePresenceMovementSpeedPercent(int value)
    {
        PresenceSettings.ValidateMovementSpeedPercent(value);
        SaveIntegerPreference(
            value,
            PresenceMovementSpeedFileName);
        ApplicationLog.Information(logger, "Saved the presence movement speed preference");
    }

    public void SavePresenceSpeechScalingEnabled(bool value)
    {
        store.WriteText(
            PresenceSpeechScalingEnabledFileName,
            value.ToString(CultureInfo.InvariantCulture));
        ApplicationLog.Information(logger, "Saved the presence speech scaling preference");
    }

    private bool? LoadBooleanPreference(string fileName, string preferenceName)
    {
        var contents = store.ReadText(fileName);
        if (contents is null)
        {
            return null;
        }

        if (!bool.TryParse(contents, out var value))
        {
            throw new InvalidDataException($"The saved {preferenceName} preference is invalid.");
        }

        return value;
    }

    public void SavePresenceSpeechScaleAmountPercent(int value)
    {
        PresenceSettings.ValidateSpeechScaleAmountPercent(value);
        SaveIntegerPreference(
            value,
            PresenceSpeechScaleAmountFileName);
        ApplicationLog.Information(logger, "Saved the presence speech scale amount preference");
    }

    public void SavePresencePosition(PresencePosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        store.WriteText(
            PresencePositionFileName,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{position.X},{position.Y}"));
        ApplicationLog.Information(logger, "Saved the presence position preference");
    }

    public void SaveResponseWindowSettings(ResponseWindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var position = settings.Position is null
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{settings.Position.X},{settings.Position.Y}");
        store.WriteLines(
            ResponseWindowFileName,
            [
                settings.AlwaysShow.ToString(CultureInfo.InvariantCulture),
                settings.Topmost.ToString(CultureInfo.InvariantCulture),
                position,
            ]);
        ApplicationLog.Information(logger, "Saved the response window preference");
    }

    private int? LoadIntegerPreference(
        string fileName,
        Action<int> validate,
        string settingName)
    {
        var contents = store.ReadText(fileName);
        if (contents is null)
        {
            return null;
        }

        var content = contents.Trim();
        if (!int.TryParse(
                content,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw new InvalidDataException($"The saved {settingName} is invalid.");
        }

        try
        {
            validate(value);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException($"The saved {settingName} is invalid.", exception);
        }

        ApplicationLog.Debug(logger, $"Loaded the {settingName} preference");
        return value;
    }

    private void SaveIntegerPreference(
        int value,
        string fileName)
    {
        store.WriteText(fileName, value.ToString(CultureInfo.InvariantCulture));
    }
}
