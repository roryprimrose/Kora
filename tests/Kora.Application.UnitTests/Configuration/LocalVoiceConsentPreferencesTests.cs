using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalVoiceConsentPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"Kora.VoiceConsent.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void First_launch_has_no_consent_and_an_endpoint_preference_cannot_supply_it()
    {
        var devices = new LocalAudioDevicePreferences(new TestPaths(root, root),
            NullLogger<LocalAudioDevicePreferences>.Instance);
        devices.SaveMicrophoneId("selected");

        Create().Load().Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Explicit_consent_or_withdrawal_survives_a_fresh_preferences_instance(bool consent)
    {
        Create().Save(consent);

        Create().Load().Should().Be(consent);
        File.Exists(Path.Combine(root, "Preferences", "voice-consent.tmp")).Should().BeFalse();
    }

    [Fact]
    public void Corrupt_consent_is_a_reported_blocker_not_granted_or_an_implicit_reset()
    {
        Directory.CreateDirectory(Path.Combine(root, "Preferences"));
        File.WriteAllText(Path.Combine(root, "Preferences", "voice-consent.txt"), "yes");

        var loading = () => Create().Load();

        loading.Should().Throw<InvalidDataException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private LocalVoiceConsentPreferences Create() =>
        new(new TestPaths(root, root), NullLogger<LocalVoiceConsentPreferences>.Instance);

    private sealed record TestPaths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
}
