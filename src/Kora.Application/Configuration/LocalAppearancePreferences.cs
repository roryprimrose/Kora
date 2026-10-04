using System.Globalization;

using Kora.Application.Diagnostics;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed class LocalAppearancePreferences(
    IApplicationDataPaths paths,
    ILogger<LocalAppearancePreferences> logger) : IAppearancePreferences
{
    private const string FileName = "appearance-theme.txt";
    private const string PresenceTimeoutFileName = "presence-timeout-seconds.txt";
    private const string PresenceSizeFileName = "presence-size-pixels.txt";
    private const string PresenceDotSizeFileName = "presence-dot-size-percent.txt";
    private const string PresenceMovementSpeedFileName = "presence-movement-speed-percent.txt";
    private const string PresencePositionFileName = "presence-position.txt";
    private const string ResponseWindowFileName = "response-window.txt";
    private const string TemporaryFileName = "appearance-theme.tmp";
    private const string PresenceTimeoutTemporaryFileName = "presence-timeout-seconds.tmp";
    private const string PresenceSizeTemporaryFileName = "presence-size-pixels.tmp";
    private const string PresenceDotSizeTemporaryFileName = "presence-dot-size-percent.tmp";
    private const string PresenceMovementSpeedTemporaryFileName = "presence-movement-speed-percent.tmp";
    private const string PresencePositionTemporaryFileName = "presence-position.tmp";
    private const string ResponseWindowTemporaryFileName = "response-window.tmp";

    private readonly string preferenceDirectory = Path.Combine(paths.LocalRoot, "Preferences");
    private readonly string preferencePath = Path.Combine(paths.LocalRoot, "Preferences", FileName);
    private readonly string presenceTimeoutPreferencePath =
        Path.Combine(paths.LocalRoot, "Preferences", PresenceTimeoutFileName);
    private readonly string presenceSizePreferencePath =
        Path.Combine(paths.LocalRoot, "Preferences", PresenceSizeFileName);
    private readonly string presenceDotSizePreferencePath =
        Path.Combine(paths.LocalRoot, "Preferences", PresenceDotSizeFileName);
    private readonly string presenceMovementSpeedPreferencePath =
        Path.Combine(paths.LocalRoot, "Preferences", PresenceMovementSpeedFileName);
    private readonly string presencePositionPreferencePath =
        Path.Combine(paths.LocalRoot, "Preferences", PresencePositionFileName);
    private readonly string responseWindowPreferencePath =
        Path.Combine(paths.LocalRoot, "Preferences", ResponseWindowFileName);

    public ApplicationThemeMode? LoadThemeMode()
    {
        if (!File.Exists(preferencePath))
        {
            return null;
        }

        var value = File.ReadAllText(preferencePath).Trim();
        var result = Enum.TryParse<ApplicationThemeMode>(value, ignoreCase: true, out var mode)
                     && Enum.IsDefined(mode)
            ? mode
            : throw new InvalidDataException("The saved appearance theme is invalid.");
        ApplicationLog.Debug(logger, "Loaded the appearance theme preference");
        return result;
    }

    public void SaveThemeMode(ApplicationThemeMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The appearance theme is invalid.");
        }

        Directory.CreateDirectory(preferenceDirectory);
        var temporaryPath = Path.Combine(preferenceDirectory, TemporaryFileName);
        File.WriteAllText(temporaryPath, mode.ToString());
        File.Move(temporaryPath, preferencePath, overwrite: true);
        ApplicationLog.Information(logger, "Saved the appearance theme preference");
    }

    public int? LoadPresenceTimeoutSeconds()
    {
        if (!File.Exists(presenceTimeoutPreferencePath))
        {
            return null;
        }

        var value = File.ReadAllText(presenceTimeoutPreferencePath).Trim();
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var seconds))
        {
            throw new InvalidDataException("The saved presence timeout is invalid.");
        }

        try
        {
            PresenceSettings.ValidateTimeoutSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("The saved presence timeout is invalid.", exception);
        }

        ApplicationLog.Debug(logger, "Loaded the presence timeout preference");
        return seconds;
    }

    public void SavePresenceTimeoutSeconds(int seconds)
    {
        PresenceSettings.ValidateTimeoutSeconds(seconds);

        SaveIntegerPreference(
            seconds,
            presenceTimeoutPreferencePath,
            PresenceTimeoutTemporaryFileName);
        ApplicationLog.Information(logger, "Saved the presence timeout preference");
    }

    public int? LoadPresenceSizePixels() =>
        LoadIntegerPreference(
            presenceSizePreferencePath,
            PresenceSettings.ValidateSizePixels,
            "presence size");

    public int? LoadPresenceDotSizePercent() =>
        LoadIntegerPreference(
            presenceDotSizePreferencePath,
            PresenceSettings.ValidateDotSizePercent,
            "presence dot size");

    public int? LoadPresenceMovementSpeedPercent() =>
        LoadIntegerPreference(
            presenceMovementSpeedPreferencePath,
            PresenceSettings.ValidateMovementSpeedPercent,
            "presence movement speed");

    public PresencePosition? LoadPresencePosition()
    {
        if (!File.Exists(presencePositionPreferencePath))
        {
            return null;
        }

        var coordinates = File.ReadAllText(presencePositionPreferencePath)
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
        if (!File.Exists(responseWindowPreferencePath))
        {
            return null;
        }

        var values = File.ReadAllLines(responseWindowPreferencePath);
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
            presenceSizePreferencePath,
            PresenceSizeTemporaryFileName);
        ApplicationLog.Information(logger, "Saved the presence size preference");
    }

    public void SavePresenceDotSizePercent(int value)
    {
        PresenceSettings.ValidateDotSizePercent(value);
        SaveIntegerPreference(
            value,
            presenceDotSizePreferencePath,
            PresenceDotSizeTemporaryFileName);
        ApplicationLog.Information(logger, "Saved the presence dot size preference");
    }

    public void SavePresenceMovementSpeedPercent(int value)
    {
        PresenceSettings.ValidateMovementSpeedPercent(value);
        SaveIntegerPreference(
            value,
            presenceMovementSpeedPreferencePath,
            PresenceMovementSpeedTemporaryFileName);
        ApplicationLog.Information(logger, "Saved the presence movement speed preference");
    }

    public void SavePresencePosition(PresencePosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        Directory.CreateDirectory(preferenceDirectory);
        var temporaryPath = Path.Combine(
            preferenceDirectory,
            PresencePositionTemporaryFileName);
        File.WriteAllText(
            temporaryPath,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{position.X},{position.Y}"));
        File.Move(
            temporaryPath,
            presencePositionPreferencePath,
            overwrite: true);
        ApplicationLog.Information(logger, "Saved the presence position preference");
    }

    public void SaveResponseWindowSettings(ResponseWindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(preferenceDirectory);
        var temporaryPath = Path.Combine(preferenceDirectory, ResponseWindowTemporaryFileName);
        var position = settings.Position is null
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{settings.Position.X},{settings.Position.Y}");
        File.WriteAllLines(
            temporaryPath,
            [
                settings.AlwaysShow.ToString(CultureInfo.InvariantCulture),
                settings.Topmost.ToString(CultureInfo.InvariantCulture),
                position,
            ]);
        File.Move(temporaryPath, responseWindowPreferencePath, overwrite: true);
        ApplicationLog.Information(logger, "Saved the response window preference");
    }

    private int? LoadIntegerPreference(
        string path,
        Action<int> validate,
        string settingName)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var content = File.ReadAllText(path).Trim();
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
        string path,
        string temporaryFileName)
    {
        Directory.CreateDirectory(preferenceDirectory);
        var temporaryPath = Path.Combine(preferenceDirectory, temporaryFileName);
        File.WriteAllText(temporaryPath, value.ToString(CultureInfo.InvariantCulture));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
