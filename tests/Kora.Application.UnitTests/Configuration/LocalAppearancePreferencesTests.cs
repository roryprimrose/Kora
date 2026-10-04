using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalAppearancePreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void LoadThemeMode_returns_null_when_no_preference_exists()
    {
        var preferences = CreatePreferences();

        preferences.LoadThemeMode().Should().BeNull();
        preferences.LoadPresenceTimeoutSeconds().Should().BeNull();
        preferences.LoadPresenceSizePixels().Should().BeNull();
        preferences.LoadPresenceDotSizePercent().Should().BeNull();
        preferences.LoadPresenceMovementSpeedPercent().Should().BeNull();
        preferences.LoadPresencePosition().Should().BeNull();
        preferences.LoadResponseWindowSettings().Should().BeNull();
    }

    [Fact]
    public void SaveThemeMode_atomically_replaces_and_loads_the_preference()
    {
        var preferences = CreatePreferences();

        preferences.SaveThemeMode(ApplicationThemeMode.Light);
        preferences.SaveThemeMode(ApplicationThemeMode.Dark);

        preferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
        File.Exists(Path.Combine(root, "Preferences", "appearance-theme.tmp")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("100")]
    public void LoadThemeMode_rejects_invalid_content(string content)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "appearance-theme.txt"), content);

        var action = CreatePreferences().LoadThemeMode;

        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SaveThemeMode_rejects_an_invalid_mode()
    {
        var action = () => CreatePreferences().SaveThemeMode((ApplicationThemeMode)100);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SavePresenceTimeoutSeconds_atomically_replaces_and_loads_the_preference()
    {
        var preferences = CreatePreferences();

        preferences.SavePresenceTimeoutSeconds(10);
        preferences.SavePresenceTimeoutSeconds(5);

        preferences.LoadPresenceTimeoutSeconds().Should().Be(5);
        File.Exists(Path.Combine(root, "Preferences", "presence-timeout-seconds.tmp")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("61")]
    public void LoadPresenceTimeoutSeconds_rejects_invalid_content(string content)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "presence-timeout-seconds.txt"), content);

        var action = CreatePreferences().LoadPresenceTimeoutSeconds;

        action.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void SavePresenceTimeoutSeconds_rejects_an_invalid_value(int value)
    {
        var action = () => CreatePreferences().SavePresenceTimeoutSeconds(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Presence_preferences_use_presence_storage_names()
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "presence-size-pixels.txt"), "400");
        File.WriteAllText(Path.Combine(directory, "presence-dot-size-percent.txt"), "120");
        File.WriteAllText(Path.Combine(directory, "presence-movement-speed-percent.txt"), "125");
        File.WriteAllText(Path.Combine(directory, "presence-position.txt"), "-400,200");
        var preferences = CreatePreferences();

        preferences.LoadPresenceSizePixels().Should().Be(400);
        preferences.LoadPresenceDotSizePercent().Should().Be(120);
        preferences.LoadPresenceMovementSpeedPercent().Should().Be(125);
        preferences.LoadPresencePosition().Should().Be(new PresencePosition(-400, 200));

        preferences.SavePresenceSizePixels(360);
        preferences.SavePresenceDotSizePercent(100);
        preferences.SavePresenceMovementSpeedPercent(100);
        preferences.SavePresencePosition(new PresencePosition(120, -80));

        File.ReadAllText(Path.Combine(directory, "presence-size-pixels.txt")).Should().Be("360");
        File.ReadAllText(Path.Combine(directory, "presence-dot-size-percent.txt")).Should().Be("100");
        File.ReadAllText(Path.Combine(directory, "presence-movement-speed-percent.txt")).Should().Be("100");
        File.ReadAllText(Path.Combine(directory, "presence-position.txt")).Should().Be("120,-80");
    }

    [Fact]
    public void SavePresenceSettings_atomically_replace_and_load_the_preferences()
    {
        var preferences = CreatePreferences();

        preferences.SavePresenceSizePixels(400);
        preferences.SavePresenceSizePixels(360);
        preferences.SavePresenceDotSizePercent(120);
        preferences.SavePresenceDotSizePercent(100);
        preferences.SavePresenceMovementSpeedPercent(125);
        preferences.SavePresenceMovementSpeedPercent(100);

        preferences.LoadPresenceSizePixels().Should().Be(360);
        preferences.LoadPresenceDotSizePercent().Should().Be(100);
        preferences.LoadPresenceMovementSpeedPercent().Should().Be(100);
        File.Exists(Path.Combine(root, "Preferences", "presence-size-pixels.tmp")).Should().BeFalse();
        File.Exists(Path.Combine(root, "Preferences", "presence-dot-size-percent.tmp")).Should().BeFalse();
        File.Exists(Path.Combine(root, "Preferences", "presence-movement-speed-percent.tmp")).Should().BeFalse();
    }

    [Fact]
    public void SavePresencePosition_atomically_replaces_and_loads_the_preference()
    {
        var preferences = CreatePreferences();

        preferences.SavePresencePosition(new PresencePosition(-400, 200));
        preferences.SavePresencePosition(new PresencePosition(120, -80));

        preferences.LoadPresencePosition().Should().Be(
            new PresencePosition(120, -80));
        File.Exists(Path.Combine(root, "Preferences", "presence-position.tmp"))
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1,2,3")]
    [InlineData("one,2")]
    [InlineData("1,two")]
    public void LoadPresencePosition_rejects_invalid_content(string content)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "presence-position.txt"), content);

        var action = CreatePreferences().LoadPresencePosition;

        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SavePresencePosition_rejects_null()
    {
        var action = () => CreatePreferences().SavePresencePosition(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SaveResponseWindowSettings_atomically_replaces_and_loads_the_preference()
    {
        var preferences = CreatePreferences();
        var initial = new ResponseWindowSettings(
            AlwaysShow: true,
            Topmost: false,
            Position: new ResponseWindowPosition(-1200, 240));
        var replacement = new ResponseWindowSettings(
            AlwaysShow: false,
            Topmost: true,
            Position: new ResponseWindowPosition(80, 120));

        preferences.SaveResponseWindowSettings(initial);
        preferences.SaveResponseWindowSettings(replacement);

        preferences.LoadResponseWindowSettings().Should().Be(replacement);
        File.Exists(Path.Combine(root, "Preferences", "response-window.tmp")).Should().BeFalse();
    }

    [Fact]
    public void SaveResponseWindowSettings_supports_an_unpositioned_window()
    {
        var preferences = CreatePreferences();

        preferences.SaveResponseWindowSettings(ResponseWindowSettings.Default);

        preferences.LoadResponseWindowSettings().Should().Be(ResponseWindowSettings.Default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("true")]
    [InlineData("true\ntrue")]
    [InlineData("invalid\ntrue\n1,2")]
    [InlineData("true\ninvalid\n1,2")]
    [InlineData("true\ntrue\n1")]
    [InlineData("true\ntrue\none,2")]
    [InlineData("true\ntrue\n1,two")]
    [InlineData("true\ntrue\n1,2,3")]
    public void LoadResponseWindowSettings_rejects_invalid_content(string content)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "response-window.txt"), content);

        var action = CreatePreferences().LoadResponseWindowSettings;

        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SaveResponseWindowSettings_rejects_null()
    {
        var action = () => CreatePreferences().SaveResponseWindowSettings(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("presence-size-pixels.txt", "", PresenceSetting.Size)]
    [InlineData("presence-size-pixels.txt", "invalid", PresenceSetting.Size)]
    [InlineData("presence-size-pixels.txt", "239", PresenceSetting.Size)]
    [InlineData("presence-size-pixels.txt", "601", PresenceSetting.Size)]
    [InlineData("presence-dot-size-percent.txt", "", PresenceSetting.DotSize)]
    [InlineData("presence-dot-size-percent.txt", "invalid", PresenceSetting.DotSize)]
    [InlineData("presence-dot-size-percent.txt", "49", PresenceSetting.DotSize)]
    [InlineData("presence-dot-size-percent.txt", "201", PresenceSetting.DotSize)]
    [InlineData("presence-movement-speed-percent.txt", "", PresenceSetting.MovementSpeed)]
    [InlineData("presence-movement-speed-percent.txt", "invalid", PresenceSetting.MovementSpeed)]
    [InlineData("presence-movement-speed-percent.txt", "24", PresenceSetting.MovementSpeed)]
    [InlineData("presence-movement-speed-percent.txt", "201", PresenceSetting.MovementSpeed)]
    public void LoadPresenceSetting_rejects_invalid_content(
        string fileName,
        string content,
        PresenceSetting setting)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
        var preferences = CreatePreferences();

        Func<int?> action = setting switch
        {
            PresenceSetting.Size => preferences.LoadPresenceSizePixels,
            PresenceSetting.DotSize => preferences.LoadPresenceDotSizePercent,
            PresenceSetting.MovementSpeed => preferences.LoadPresenceMovementSpeedPercent,
            _ => throw new ArgumentOutOfRangeException(nameof(setting)),
        };

        action.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(PresenceSetting.Size, PresenceSettings.MinimumSizePixels - 1)]
    [InlineData(PresenceSetting.Size, PresenceSettings.MaximumSizePixels + 1)]
    [InlineData(PresenceSetting.DotSize, PresenceSettings.MinimumDotSizePercent - 1)]
    [InlineData(PresenceSetting.DotSize, PresenceSettings.MaximumDotSizePercent + 1)]
    [InlineData(PresenceSetting.MovementSpeed, PresenceSettings.MinimumMovementSpeedPercent - 1)]
    [InlineData(PresenceSetting.MovementSpeed, PresenceSettings.MaximumMovementSpeedPercent + 1)]
    public void SavePresenceSetting_rejects_an_invalid_value(
        PresenceSetting setting,
        int value)
    {
        var preferences = CreatePreferences();

        var action = () =>
        {
            switch (setting)
            {
                case PresenceSetting.Size:
                    preferences.SavePresenceSizePixels(value);
                    break;
                case PresenceSetting.DotSize:
                    preferences.SavePresenceDotSizePercent(value);
                    break;
                case PresenceSetting.MovementSpeed:
                    preferences.SavePresenceMovementSpeedPercent(value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(setting));
            }
        };

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private LocalAppearancePreferences CreatePreferences() =>
        new(
            new TestPaths(root, Path.Combine(root, "Roaming")),
            NullLogger<LocalAppearancePreferences>.Instance);

    private sealed record TestPaths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;

    public enum PresenceSetting
    {
        Size,
        DotSize,
        MovementSpeed,
    }
}
