using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Auditing;

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
        preferences.LoadResponseTimeoutSeconds().Should().BeNull();
        preferences.LoadPresenceSizePixels().Should().BeNull();
        preferences.LoadPresenceDotSizePercent().Should().BeNull();
        preferences.LoadPresenceDotDensityPercent().Should().BeNull();
        preferences.LoadPresenceMovementSpeedPercent().Should().BeNull();
        preferences.LoadPresenceSpeechScalingEnabled().Should().BeNull();
        preferences.LoadPresenceSpeechScaleAmountPercent().Should().BeNull();
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

    [Fact]
    public void Shared_registry_commits_one_real_atomic_file_and_restores_via_existing_domain_format()
    {
        var preferences = CreatePreferences();
        var service = new AppearanceConfigurationService(preferences, new AppearanceAudit(),
            NullLogger<AppearanceConfigurationService>.Instance);
        var before = service.Get(AppearanceOption.ResponseTimeout);
        var result = service.Apply(service.Propose(AppearanceOption.ResponseTimeout, new AppearanceValue.Number(15),
            before.Revision, SecurityAuditInitiator.TypedCommand), TestContext.Current.CancellationToken);
        result.Succeeded.Should().BeTrue();
        Directory.GetFiles(Path.Combine(root, "Preferences")).Should().ContainSingle();
        File.ReadAllText(Path.Combine(root, "Preferences", "response-timeout-seconds.txt")).Should().Be("15");
        var restored = new AppearanceConfigurationService(CreatePreferences(), new AppearanceAudit(),
            NullLogger<AppearanceConfigurationService>.Instance);
        restored.Get(AppearanceOption.ResponseTimeout).Value.Should().Be(new AppearanceValue.Number(15));
        restored.Get(AppearanceOption.PresenceTimeout).Value.Should().Be(new AppearanceValue.Number(PresenceSettings.DefaultTimeoutSeconds));
        result = service.Apply(service.ProposeReset(AppearanceOption.ResponseTimeout, result.State.Revision,
            SecurityAuditInitiator.LocalUser), TestContext.Current.CancellationToken);
        result.Succeeded.Should().BeTrue();
        preferences.LoadResponseTimeoutSeconds().Should().Be(ResponseWindowSettings.DefaultTimeoutSeconds);
        Directory.GetFiles(Path.Combine(root, "Preferences")).Should().ContainSingle();
    }

    private sealed class AppearanceAudit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
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
        File.Exists(Path.Combine(root, "Preferences", "presence-inactivity-timeout-seconds.tmp"))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public void LoadPresenceTimeoutSeconds_rejects_invalid_content(string content)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "presence-inactivity-timeout-seconds.txt"), content);

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
    public void Missing_timeouts_use_independent_defaults_without_writing_preferences()
    {
        var preferences = CreatePreferences();

        (preferences.LoadPresenceTimeoutSeconds() ?? PresenceSettings.DefaultTimeoutSeconds)
            .Should().Be(10);
        (preferences.LoadResponseTimeoutSeconds() ?? ResponseWindowSettings.DefaultTimeoutSeconds)
            .Should().Be(5);
        Directory.Exists(Path.Combine(root, "Preferences")).Should().BeFalse();
    }

    [Fact]
    public void Presence_and_response_timeouts_round_trip_independently_after_restart()
    {
        var preferences = CreatePreferences();
        preferences.SavePresenceTimeoutSeconds(15);
        preferences.SaveResponseTimeoutSeconds(8);
        preferences.SavePresenceTimeoutSeconds(20);

        var reloadedPreferences = CreatePreferences();

        reloadedPreferences.LoadPresenceTimeoutSeconds().Should().Be(20);
        reloadedPreferences.LoadResponseTimeoutSeconds().Should().Be(8);
        reloadedPreferences.SaveResponseTimeoutSeconds(12);
        reloadedPreferences.LoadPresenceTimeoutSeconds().Should().Be(20);
        var directory = Path.Combine(root, "Preferences");
        File.ReadAllText(Path.Combine(directory, "presence-inactivity-timeout-seconds.txt"))
            .Should().Be("20");
        File.ReadAllText(Path.Combine(directory, "response-timeout-seconds.txt")).Should().Be("12");
        Directory.GetFiles(directory).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(60)]
    public void Both_timeouts_round_trip_supported_values(int value)
    {
        var preferences = CreatePreferences();

        preferences.SavePresenceTimeoutSeconds(value);
        preferences.SaveResponseTimeoutSeconds(value);

        preferences.LoadPresenceTimeoutSeconds().Should().Be(value);
        preferences.LoadResponseTimeoutSeconds().Should().Be(value);
    }

    [Fact]
    public void Saving_response_timeout_atomically_replaces_only_its_canonical_preference()
    {
        var preferences = CreatePreferences();

        preferences.SaveResponseTimeoutSeconds(18);
        preferences.SaveResponseTimeoutSeconds(8);

        preferences.LoadResponseTimeoutSeconds().Should().Be(8);
        preferences.LoadPresenceTimeoutSeconds().Should().BeNull();
        var directory = Path.Combine(root, "Preferences");
        Directory.GetFiles(directory).Should().ContainSingle().Which
            .Should().Be(Path.Combine(directory, "response-timeout-seconds.txt"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(18)]
    [InlineData(60)]
    public void Legacy_timeout_is_only_a_response_fallback_and_loading_does_not_migrate_files(int value)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        var legacyPath = Path.Combine(directory, "presence-timeout-seconds.txt");
        File.WriteAllText(legacyPath, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var preferences = CreatePreferences();

        preferences.LoadResponseTimeoutSeconds().Should().Be(value);
        preferences.LoadPresenceTimeoutSeconds().Should().BeNull();
        (preferences.LoadPresenceTimeoutSeconds() ?? PresenceSettings.DefaultTimeoutSeconds)
            .Should().Be(10);
        File.ReadAllText(legacyPath).Should()
            .Be(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.GetFiles(directory).Should().ContainSingle().Which.Should().Be(legacyPath);
    }

    [Theory]
    [InlineData("18")]
    [InlineData("invalid")]
    public void Canonical_response_timeout_takes_priority_over_legacy_content(string legacyContent)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "presence-timeout-seconds.txt"), legacyContent);
        File.WriteAllText(Path.Combine(directory, "response-timeout-seconds.txt"), "8");
        var preferences = CreatePreferences();

        preferences.LoadResponseTimeoutSeconds().Should().Be(8);
        preferences.LoadPresenceTimeoutSeconds().Should().BeNull();
    }

    [Fact]
    public void Saving_either_timeout_only_writes_its_canonical_file_and_preserves_legacy_response()
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        var legacyPath = Path.Combine(directory, "presence-timeout-seconds.txt");
        File.WriteAllText(legacyPath, "18");
        var preferences = CreatePreferences();

        preferences.SavePresenceTimeoutSeconds(25);

        preferences.LoadResponseTimeoutSeconds().Should().Be(18);
        File.Exists(Path.Combine(directory, "response-timeout-seconds.txt")).Should().BeFalse();
        File.ReadAllText(legacyPath).Should().Be("18");

        preferences.SaveResponseTimeoutSeconds(8);

        preferences.LoadPresenceTimeoutSeconds().Should().Be(25);
        File.ReadAllText(Path.Combine(directory, "presence-inactivity-timeout-seconds.txt"))
            .Should().Be("25");
        File.ReadAllText(Path.Combine(directory, "response-timeout-seconds.txt")).Should().Be("8");
        File.ReadAllText(legacyPath).Should().Be("18");
        CreatePreferences().LoadResponseTimeoutSeconds().Should().Be(8);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public void Invalid_canonical_response_timeout_does_not_fall_back_to_valid_legacy(string content)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "response-timeout-seconds.txt"), content);
        File.WriteAllText(Path.Combine(directory, "presence-timeout-seconds.txt"), "18");

        var action = CreatePreferences().LoadResponseTimeoutSeconds;

        action.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public void Invalid_legacy_response_timeout_is_rejected_but_does_not_affect_presence(string content)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "presence-timeout-seconds.txt"), content);
        var preferences = CreatePreferences();

        var action = preferences.LoadResponseTimeoutSeconds;

        action.Should().Throw<InvalidDataException>();
        preferences.LoadPresenceTimeoutSeconds().Should().BeNull();
    }

    [Fact]
    public void Invalid_presence_timeout_does_not_change_the_independent_response_timeout()
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "presence-inactivity-timeout-seconds.txt"), "invalid");
        File.WriteAllText(Path.Combine(directory, "response-timeout-seconds.txt"), "8");
        var preferences = CreatePreferences();

        var action = preferences.LoadPresenceTimeoutSeconds;

        action.Should().Throw<InvalidDataException>();
        preferences.LoadResponseTimeoutSeconds().Should().Be(8);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void SaveResponseTimeoutSeconds_rejects_invalid_values_without_writing(int value)
    {
        var action = () => CreatePreferences().SaveResponseTimeoutSeconds(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
        Directory.Exists(Path.Combine(root, "Preferences")).Should().BeFalse();
    }

    [Fact]
    public void Presence_preferences_use_presence_storage_names()
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "presence-size-pixels.txt"), "400");
        File.WriteAllText(Path.Combine(directory, "presence-dot-size-percent.txt"), "120");
        File.WriteAllText(Path.Combine(directory, "presence-dot-density-percent.txt"), "125");
        File.WriteAllText(Path.Combine(directory, "presence-movement-speed-percent.txt"), "125");
        File.WriteAllText(Path.Combine(directory, "presence-speech-scaling-enabled.txt"), "False");
        File.WriteAllText(Path.Combine(directory, "presence-speech-scale-amount-percent.txt"), "150");
        File.WriteAllText(Path.Combine(directory, "presence-position.txt"), "-400,200");
        var preferences = CreatePreferences();

        preferences.LoadPresenceSizePixels().Should().Be(400);
        preferences.LoadPresenceDotSizePercent().Should().Be(120);
        preferences.LoadPresenceDotDensityPercent().Should().Be(125);
        preferences.LoadPresenceMovementSpeedPercent().Should().Be(125);
        preferences.LoadPresenceSpeechScalingEnabled().Should().BeFalse();
        preferences.LoadPresenceSpeechScaleAmountPercent().Should().Be(150);
        preferences.LoadPresencePosition().Should().Be(new PresencePosition(-400, 200));

        preferences.SavePresenceSizePixels(360);
        preferences.SavePresenceDotSizePercent(100);
        preferences.SavePresenceDotDensityPercent(100);
        preferences.SavePresenceMovementSpeedPercent(100);
        preferences.SavePresenceSpeechScalingEnabled(true);
        preferences.SavePresenceSpeechScaleAmountPercent(100);
        preferences.SavePresencePosition(new PresencePosition(120, -80));

        File.ReadAllText(Path.Combine(directory, "presence-size-pixels.txt")).Should().Be("360");
        File.ReadAllText(Path.Combine(directory, "presence-dot-size-percent.txt")).Should().Be("100");
        File.ReadAllText(Path.Combine(directory, "presence-dot-density-percent.txt")).Should().Be("100");
        File.ReadAllText(Path.Combine(directory, "presence-movement-speed-percent.txt")).Should().Be("100");
        File.ReadAllText(Path.Combine(directory, "presence-speech-scaling-enabled.txt")).Should().Be("True");
        File.ReadAllText(Path.Combine(directory, "presence-speech-scale-amount-percent.txt")).Should().Be("100");
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
        preferences.SavePresenceDotDensityPercent(125);
        preferences.SavePresenceDotDensityPercent(100);
        preferences.SavePresenceMovementSpeedPercent(125);
        preferences.SavePresenceMovementSpeedPercent(100);
        preferences.SavePresenceSpeechScalingEnabled(false);
        preferences.SavePresenceSpeechScalingEnabled(true);
        preferences.SavePresenceSpeechScaleAmountPercent(150);
        preferences.SavePresenceSpeechScaleAmountPercent(100);

        preferences.LoadPresenceSizePixels().Should().Be(360);
        preferences.LoadPresenceDotSizePercent().Should().Be(100);
        preferences.LoadPresenceDotDensityPercent().Should().Be(100);
        preferences.LoadPresenceMovementSpeedPercent().Should().Be(100);
        preferences.LoadPresenceSpeechScalingEnabled().Should().BeTrue();
        preferences.LoadPresenceSpeechScaleAmountPercent().Should().Be(100);
        File.Exists(Path.Combine(root, "Preferences", "presence-size-pixels.tmp")).Should().BeFalse();
        File.Exists(Path.Combine(root, "Preferences", "presence-dot-size-percent.tmp")).Should().BeFalse();
        File.Exists(Path.Combine(root, "Preferences", "presence-dot-density-percent.tmp")).Should().BeFalse();
        File.Exists(Path.Combine(root, "Preferences", "presence-movement-speed-percent.tmp")).Should().BeFalse();
        File.Exists(Path.Combine(root, "Preferences", "presence-speech-scaling-enabled.tmp")).Should().BeFalse();
        File.Exists(Path.Combine(root, "Preferences", "presence-speech-scale-amount-percent.tmp")).Should().BeFalse();
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SavePresenceSpeechScalingEnabled_loads_both_values(bool value)
    {
        var preferences = CreatePreferences();

        preferences.SavePresenceSpeechScalingEnabled(value);

        preferences.LoadPresenceSpeechScalingEnabled().Should().Be(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("true,false")]
    public void LoadPresenceSpeechScalingEnabled_rejects_invalid_content(string content)
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "presence-speech-scaling-enabled.txt"), content);

        var action = CreatePreferences().LoadPresenceSpeechScalingEnabled;

        action.Should().Throw<InvalidDataException>();
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
    [InlineData("presence-dot-density-percent.txt", "", PresenceSetting.DotDensity)]
    [InlineData("presence-dot-density-percent.txt", "invalid", PresenceSetting.DotDensity)]
    [InlineData("presence-dot-density-percent.txt", "24", PresenceSetting.DotDensity)]
    [InlineData("presence-dot-density-percent.txt", "201", PresenceSetting.DotDensity)]
    [InlineData("presence-dot-density-percent.txt", "25.5", PresenceSetting.DotDensity)]
    [InlineData("presence-dot-density-percent.txt", "2147483648", PresenceSetting.DotDensity)]
    [InlineData("presence-movement-speed-percent.txt", "", PresenceSetting.MovementSpeed)]
    [InlineData("presence-movement-speed-percent.txt", "invalid", PresenceSetting.MovementSpeed)]
    [InlineData("presence-movement-speed-percent.txt", "24", PresenceSetting.MovementSpeed)]
    [InlineData("presence-movement-speed-percent.txt", "201", PresenceSetting.MovementSpeed)]
    [InlineData("presence-speech-scale-amount-percent.txt", "", PresenceSetting.SpeechScaleAmount)]
    [InlineData("presence-speech-scale-amount-percent.txt", "invalid", PresenceSetting.SpeechScaleAmount)]
    [InlineData("presence-speech-scale-amount-percent.txt", "-1", PresenceSetting.SpeechScaleAmount)]
    [InlineData("presence-speech-scale-amount-percent.txt", "201", PresenceSetting.SpeechScaleAmount)]
    [InlineData("presence-speech-scale-amount-percent.txt", "100.5", PresenceSetting.SpeechScaleAmount)]
    [InlineData("presence-speech-scale-amount-percent.txt", "2147483648", PresenceSetting.SpeechScaleAmount)]
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
            PresenceSetting.DotDensity => preferences.LoadPresenceDotDensityPercent,
            PresenceSetting.MovementSpeed => preferences.LoadPresenceMovementSpeedPercent,
            PresenceSetting.SpeechScaleAmount => preferences.LoadPresenceSpeechScaleAmountPercent,
            _ => throw new ArgumentOutOfRangeException(nameof(setting)),
        };

        action.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(PresenceSetting.Size, PresenceSettings.MinimumSizePixels - 1)]
    [InlineData(PresenceSetting.Size, PresenceSettings.MaximumSizePixels + 1)]
    [InlineData(PresenceSetting.DotSize, PresenceSettings.MinimumDotSizePercent - 1)]
    [InlineData(PresenceSetting.DotSize, PresenceSettings.MaximumDotSizePercent + 1)]
    [InlineData(PresenceSetting.DotDensity, PresenceSettings.MinimumDotDensityPercent - 1)]
    [InlineData(PresenceSetting.DotDensity, PresenceSettings.MaximumDotDensityPercent + 1)]
    [InlineData(PresenceSetting.MovementSpeed, PresenceSettings.MinimumMovementSpeedPercent - 1)]
    [InlineData(PresenceSetting.MovementSpeed, PresenceSettings.MaximumMovementSpeedPercent + 1)]
    [InlineData(PresenceSetting.SpeechScaleAmount, PresenceSettings.MinimumSpeechScaleAmountPercent - 1)]
    [InlineData(PresenceSetting.SpeechScaleAmount, PresenceSettings.MaximumSpeechScaleAmountPercent + 1)]
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
                case PresenceSetting.DotDensity:
                    preferences.SavePresenceDotDensityPercent(value);
                    break;
                case PresenceSetting.MovementSpeed:
                    preferences.SavePresenceMovementSpeedPercent(value);
                    break;
                case PresenceSetting.SpeechScaleAmount:
                    preferences.SavePresenceSpeechScaleAmountPercent(value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(setting));
            }
        };

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(PresenceSetting.DotDensity, PresenceSettings.MinimumDotDensityPercent)]
    [InlineData(PresenceSetting.DotDensity, PresenceSettings.DefaultDotDensityPercent)]
    [InlineData(PresenceSetting.DotDensity, PresenceSettings.MaximumDotDensityPercent)]
    [InlineData(PresenceSetting.SpeechScaleAmount, PresenceSettings.MinimumSpeechScaleAmountPercent)]
    [InlineData(PresenceSetting.SpeechScaleAmount, PresenceSettings.DefaultSpeechScaleAmountPercent)]
    [InlineData(PresenceSetting.SpeechScaleAmount, PresenceSettings.MaximumSpeechScaleAmountPercent)]
    public void New_presence_settings_round_trip_supported_values(
        PresenceSetting setting,
        int value)
    {
        var preferences = CreatePreferences();

        if (setting == PresenceSetting.DotDensity)
        {
            preferences.SavePresenceDotDensityPercent(value);
            preferences.LoadPresenceDotDensityPercent().Should().Be(value);
        }
        else
        {
            preferences.SavePresenceSpeechScaleAmountPercent(value);
            preferences.LoadPresenceSpeechScaleAmountPercent().Should().Be(value);
        }
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
        DotDensity,
        MovementSpeed,
        SpeechScaleAmount,
    }
}
