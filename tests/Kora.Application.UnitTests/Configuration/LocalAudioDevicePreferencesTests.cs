using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalAudioDevicePreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void Load_returns_null_when_device_preferences_do_not_exist()
    {
        var preferences = CreatePreferences();

        preferences.LoadMicrophoneId().Should().BeNull();
        preferences.LoadOutputDeviceId().Should().BeNull();
    }

    [Fact]
    public void Save_atomically_replaces_and_loads_device_preferences()
    {
        var preferences = CreatePreferences();

        preferences.SaveMicrophoneId("microphone-first");
        preferences.SaveMicrophoneId("microphone-second");
        preferences.SaveOutputDeviceId("output-first");
        preferences.SaveOutputDeviceId("output-second");

        preferences.LoadMicrophoneId().Should().Be("microphone-second");
        preferences.LoadOutputDeviceId().Should().Be("output-second");
        File.Exists(Path.Combine(root, "Preferences", "microphone-id.tmp")).Should().BeFalse();
        File.Exists(Path.Combine(root, "Preferences", "output-device-id.tmp")).Should().BeFalse();
    }

    [Fact]
    public void Clear_removes_device_overrides_and_is_idempotent()
    {
        var preferences = CreatePreferences();
        preferences.SaveMicrophoneId("microphone");
        preferences.SaveOutputDeviceId("output");

        preferences.ClearMicrophoneId();
        preferences.ClearOutputDeviceId();
        preferences.ClearMicrophoneId();
        preferences.ClearOutputDeviceId();

        preferences.LoadMicrophoneId().Should().BeNull();
        preferences.LoadOutputDeviceId().Should().BeNull();
    }

    [Theory]
    [InlineData("microphone-id.txt", "microphone", true)]
    [InlineData("output-device-id.txt", "audio output", false)]
    public void Load_rejects_an_empty_device_preference(
        string fileName,
        string deviceType,
        bool isMicrophone)
    {
        var preferences = CreatePreferences();
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), " ");

        Func<string?> action = isMicrophone
            ? preferences.LoadMicrophoneId
            : preferences.LoadOutputDeviceId;

        action.Should().Throw<InvalidDataException>()
            .WithMessage($"*{deviceType}*");
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(" ", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    public void Save_rejects_a_blank_device_identifier(
        string identifier,
        bool isMicrophone)
    {
        var preferences = CreatePreferences();

        Action action = isMicrophone
            ? () => preferences.SaveMicrophoneId(identifier)
            : () => preferences.SaveOutputDeviceId(identifier);

        action.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private LocalAudioDevicePreferences CreatePreferences() =>
        new(
            new TestPaths(root, Path.Combine(root, "Roaming")),
            NullLogger<LocalAudioDevicePreferences>.Instance);

    private sealed record TestPaths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
}
