using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalResponseOutputPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void LoadDefaultMode_returns_null_when_no_preference_exists()
    {
        var preferences = CreatePreferences();

        preferences.LoadDefaultMode().Should().BeNull();
    }

    [Fact]
    public void SaveDefaultMode_atomically_replaces_and_loads_the_preference()
    {
        var preferences = CreatePreferences();

        preferences.SaveDefaultMode(ResponseOutputMode.VoiceOnly);
        preferences.SaveDefaultMode(ResponseOutputMode.VisualOnly);

        preferences.LoadDefaultMode().Should().Be(ResponseOutputMode.VisualOnly);
        File.Exists(Path.Combine(root, "Preferences", "response-output-mode.tmp")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("100")]
    public void LoadDefaultMode_rejects_invalid_content(string content)
    {
        var preferences = CreatePreferences();
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "response-output-mode.txt"), content);

        var action = preferences.LoadDefaultMode;

        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SaveDefaultMode_rejects_an_invalid_mode()
    {
        var preferences = CreatePreferences();

        var action = () => preferences.SaveDefaultMode((ResponseOutputMode)100);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LoadMutedOutputVisualFallback_returns_null_when_no_preference_exists()
    {
        CreatePreferences().LoadMutedOutputVisualFallback().Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Muted_output_fallback_is_persisted_independently_of_the_response_mode(bool enabled)
    {
        var preferences = CreatePreferences();
        preferences.SaveDefaultMode(ResponseOutputMode.VoiceOnly);
        preferences.SaveMutedOutputVisualFallback(!enabled);
        preferences.SaveMutedOutputVisualFallback(enabled);

        var reloaded = CreatePreferences();
        reloaded.LoadMutedOutputVisualFallback().Should().Be(enabled);
        reloaded.LoadDefaultMode().Should().Be(ResponseOutputMode.VoiceOnly);
        File.Exists(Path.Combine(root, "Preferences", "muted-output-visual-fallback.tmp")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("2")]
    public void LoadMutedOutputVisualFallback_rejects_invalid_content(string content)
    {
        var preferences = CreatePreferences();
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "muted-output-visual-fallback.txt"), content);

        var action = preferences.LoadMutedOutputVisualFallback;

        action.Should().Throw<InvalidDataException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private LocalResponseOutputPreferences CreatePreferences() =>
        new(
            new TestPaths(root, Path.Combine(root, "Roaming")),
            NullLogger<LocalResponseOutputPreferences>.Instance);

    private sealed record TestPaths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
}