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
        preferences.LoadConstellationSizePixels().Should().BeNull();
        preferences.LoadConstellationDotSizePercent().Should().BeNull();
        preferences.LoadConstellationMovementSpeedPercent().Should().BeNull();
        preferences.LoadConstellationPosition().Should().BeNull();
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
    public void SaveConstellationSettings_atomically_replace_and_load_the_preferences()
    {
        var preferences = CreatePreferences();

        preferences.SaveConstellationSizePixels(400);
        preferences.SaveConstellationSizePixels(360);
        preferences.SaveConstellationDotSizePercent(120);
        preferences.SaveConstellationDotSizePercent(100);
        preferences.SaveConstellationMovementSpeedPercent(125);
        preferences.SaveConstellationMovementSpeedPercent(100);

        preferences.LoadConstellationSizePixels().Should().Be(360);
        preferences.LoadConstellationDotSizePercent().Should().Be(100);
        preferences.LoadConstellationMovementSpeedPercent().Should().Be(100);
        File.Exists(Path.Combine(root, "Preferences", "constellation-size-pixels.tmp")).Should().BeFalse();
        File.Exists(Path.Combine(root, "Preferences", "constellation-dot-size-percent.tmp")).Should().BeFalse();
        File.Exists(Path.Combine(root, "Preferences", "constellation-movement-speed-percent.tmp")).Should().BeFalse();
    }

    [Fact]
    public void SaveConstellationPosition_atomically_replaces_and_loads_the_preference()
    {
        var preferences = CreatePreferences();

        preferences.SaveConstellationPosition(new ConstellationPosition(-400, 200));
        preferences.SaveConstellationPosition(new ConstellationPosition(120, -80));

        preferences.LoadConstellationPosition().Should().Be(
            new ConstellationPosition(120, -80));
        File.Exists(Path.Combine(root, "Preferences", "constellation-position.tmp"))
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1,2,3")]
    [InlineData("one,2")]
    [InlineData("1,two")]
    public void LoadConstellationPosition_rejects_invalid_content(string content)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "constellation-position.txt"), content);

        var action = CreatePreferences().LoadConstellationPosition;

        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SaveConstellationPosition_rejects_null()
    {
        var action = () => CreatePreferences().SaveConstellationPosition(null!);

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
    [InlineData("constellation-size-pixels.txt", "", ConstellationSetting.Size)]
    [InlineData("constellation-size-pixels.txt", "invalid", ConstellationSetting.Size)]
    [InlineData("constellation-size-pixels.txt", "239", ConstellationSetting.Size)]
    [InlineData("constellation-size-pixels.txt", "601", ConstellationSetting.Size)]
    [InlineData("constellation-dot-size-percent.txt", "", ConstellationSetting.DotSize)]
    [InlineData("constellation-dot-size-percent.txt", "invalid", ConstellationSetting.DotSize)]
    [InlineData("constellation-dot-size-percent.txt", "49", ConstellationSetting.DotSize)]
    [InlineData("constellation-dot-size-percent.txt", "201", ConstellationSetting.DotSize)]
    [InlineData("constellation-movement-speed-percent.txt", "", ConstellationSetting.MovementSpeed)]
    [InlineData("constellation-movement-speed-percent.txt", "invalid", ConstellationSetting.MovementSpeed)]
    [InlineData("constellation-movement-speed-percent.txt", "24", ConstellationSetting.MovementSpeed)]
    [InlineData("constellation-movement-speed-percent.txt", "201", ConstellationSetting.MovementSpeed)]
    public void LoadConstellationSetting_rejects_invalid_content(
        string fileName,
        string content,
        ConstellationSetting setting)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
        var preferences = CreatePreferences();

        Func<int?> action = setting switch
        {
            ConstellationSetting.Size => preferences.LoadConstellationSizePixels,
            ConstellationSetting.DotSize => preferences.LoadConstellationDotSizePercent,
            ConstellationSetting.MovementSpeed => preferences.LoadConstellationMovementSpeedPercent,
            _ => throw new ArgumentOutOfRangeException(nameof(setting)),
        };

        action.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(ConstellationSetting.Size, ConstellationSettings.MinimumSizePixels - 1)]
    [InlineData(ConstellationSetting.Size, ConstellationSettings.MaximumSizePixels + 1)]
    [InlineData(ConstellationSetting.DotSize, ConstellationSettings.MinimumDotSizePercent - 1)]
    [InlineData(ConstellationSetting.DotSize, ConstellationSettings.MaximumDotSizePercent + 1)]
    [InlineData(ConstellationSetting.MovementSpeed, ConstellationSettings.MinimumMovementSpeedPercent - 1)]
    [InlineData(ConstellationSetting.MovementSpeed, ConstellationSettings.MaximumMovementSpeedPercent + 1)]
    public void SaveConstellationSetting_rejects_an_invalid_value(
        ConstellationSetting setting,
        int value)
    {
        var preferences = CreatePreferences();

        var action = () =>
        {
            switch (setting)
            {
                case ConstellationSetting.Size:
                    preferences.SaveConstellationSizePixels(value);
                    break;
                case ConstellationSetting.DotSize:
                    preferences.SaveConstellationDotSizePercent(value);
                    break;
                case ConstellationSetting.MovementSpeed:
                    preferences.SaveConstellationMovementSpeedPercent(value);
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

    public enum ConstellationSetting
    {
        Size,
        DotSize,
        MovementSpeed,
    }
}
