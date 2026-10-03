using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Communication;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalCallAwarePreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void Load_returns_null_when_no_preference_exists()
    {
        var preferences = CreatePreferences();

        preferences.Load().Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Save_atomically_replaces_and_loads_all_setting_combinations(
        bool showVisualText,
        bool allowVoiceActivation)
    {
        var preferences = CreatePreferences();
        var settings = new CallAwareSettings(showVisualText, allowVoiceActivation);

        preferences.Save(CallAwareSettings.Default);
        preferences.Save(settings);

        preferences.Load().Should().Be(settings);
        File.Exists(Path.Combine(root, "Preferences", "call-aware-settings.tmp")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("true,true")]
    [InlineData("1")]
    [InlineData("1,1,1")]
    public void Load_rejects_invalid_content(string content)
    {
        var preferences = CreatePreferences();
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "call-aware-settings.txt"), content);

        var action = preferences.Load;

        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Save_rejects_null_settings()
    {
        var preferences = CreatePreferences();

        var action = () => preferences.Save(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private LocalCallAwarePreferences CreatePreferences() =>
        new(
            new TestPaths(root, Path.Combine(root, "Roaming")),
            NullLogger<LocalCallAwarePreferences>.Instance);

    private sealed record TestPaths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
}
