using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalTextToSpeechPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void LoadVoiceId_returns_null_when_no_preference_exists()
    {
        var preferences = CreatePreferences();

        var result = preferences.LoadVoiceId();

        result.Should().BeNull();
    }

    [Fact]
    public void LoadProviderId_returns_null_when_no_preference_exists()
    {
        var preferences = CreatePreferences();

        var result = preferences.LoadProviderId();

        result.Should().BeNull();
    }

    [Fact]
    public void SaveVoiceId_atomically_replaces_and_loads_the_preference()
    {
        var preferences = CreatePreferences();

        preferences.SaveVoiceId("first");
        preferences.SaveVoiceId("second");

        preferences.LoadVoiceId().Should().Be("second");
        File.Exists(Path.Combine(root, "Preferences", "speech-voice.tmp")).Should().BeFalse();
    }

    [Fact]
    public void SaveProviderId_atomically_replaces_and_loads_the_preference()
    {
        var preferences = CreatePreferences();

        preferences.SaveProviderId("windows");
        preferences.SaveProviderId("kokoro");

        preferences.LoadProviderId().Should().Be("kokoro");
        File.Exists(Path.Combine(root, "Preferences", "speech-provider.tmp")).Should().BeFalse();
    }

    [Fact]
    public void LoadVoiceId_rejects_an_empty_preference()
    {
        var preferences = CreatePreferences();
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "speech-voice.txt"), " ");

        var action = preferences.LoadVoiceId;

        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void LoadProviderId_rejects_an_empty_preference()
    {
        var preferences = CreatePreferences();
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "speech-provider.txt"), " ");

        var action = preferences.LoadProviderId;

        action.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void SaveVoiceId_rejects_a_blank_identifier(string voiceId)
    {
        var preferences = CreatePreferences();

        var action = () => preferences.SaveVoiceId(voiceId);

        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void SaveProviderId_rejects_a_blank_identifier(string providerId)
    {
        var preferences = CreatePreferences();

        var action = () => preferences.SaveProviderId(providerId);

        action.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private LocalTextToSpeechPreferences CreatePreferences() =>
        new(
            new TestPaths(root, Path.Combine(root, "Roaming")),
            NullLogger<LocalTextToSpeechPreferences>.Instance);

    private sealed record TestPaths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
}