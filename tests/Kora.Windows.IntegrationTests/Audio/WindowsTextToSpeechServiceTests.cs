using AwesomeAssertions;

using Kora.Core.Voice;
using Kora.Windows.Audio;

using Neovolve.Logging.Xunit;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class WindowsTextToSpeechServiceTests(
    ITestOutputHelper output) : LoggingTestsBase<WindowsTextToSpeechService>(output)
{
    [Fact]
    public async Task StopAsync_is_idempotent_before_playback_starts()
    {
        await using var service = new WindowsTextToSpeechService(Logger);

        await service.StopAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        service.IsSpeaking.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SpeakAsync_rejects_blank_text(string text)
    {
        await using var service = new WindowsTextToSpeechService(Logger);
        var voice = new SpeechVoice("test", "Test", "en-US", SpeechVoiceGender.Female);
        var outputDevice = new AudioOutputDevice("0", "Test output");

        var action = () => service.SpeakAsync(text, voice, outputDevice);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SpeakAsync_rejects_a_null_voice()
    {
        await using var service = new WindowsTextToSpeechService(Logger);
        var outputDevice = new AudioOutputDevice("0", "Test output");

        var action = () => service.SpeakAsync("Hello", null!, outputDevice);

        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SpeakAsync_rejects_a_null_output_device()
    {
        await using var service = new WindowsTextToSpeechService(Logger);
        var voice = new SpeechVoice("test", "Test", "en-US", SpeechVoiceGender.Female);

        var action = () => service.SpeakAsync("Hello", voice, null!);

        await action.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Disposed_service_rejects_voice_enumeration()
    {
        var service = new WindowsTextToSpeechService(Logger);
        await service.DisposeAsync();

        var action = service.GetVoices;

        action.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Disposed_service_rejects_output_device_enumeration()
    {
        var service = new WindowsTextToSpeechService(Logger);
        await service.DisposeAsync();

        var action = service.GetOutputDevices;

        action.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Provider_catalog_contains_the_builtin_Windows_provider()
    {
        await using var service = new WindowsTextToSpeechService(Logger);

        var providers = service.GetProviders();

        providers.Should().ContainSingle();
        providers[0].Should().Match<SpeechProvider>(
            provider => string.Equals(
                    provider.Id,
                    SpeechProviderIds.Windows,
                    StringComparison.Ordinal)
                && provider.IsBuiltIn
                && provider.IsInstalled);
    }

    [Theory]
    [InlineData(SpeechProviderIds.Windows)]
    [InlineData(SpeechProviderIds.Kokoro)]
    [InlineData("unknown")]
    public async Task Service_without_an_optional_provider_rejects_install_and_remove(
        string providerId)
    {
        await using var service = new WindowsTextToSpeechService(Logger);
        var progress = new Progress<SpeechProviderInstallProgress>();

        var install = () => service.InstallProviderAsync(
            providerId,
            progress,
            TestContext.Current.CancellationToken);
        var remove = () => service.RemoveProviderAsync(
            providerId,
            TestContext.Current.CancellationToken);

        await install.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await remove.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [WindowsFact]
    public async Task Enumerated_voices_have_stable_metadata()
    {
        await using var service = new WindowsTextToSpeechService(Logger);

        var voices = service.GetVoices();

        voices.Should().OnlyContain(voice =>
            !string.IsNullOrWhiteSpace(voice.Id) &&
            !string.IsNullOrWhiteSpace(voice.Name) &&
            !string.IsNullOrWhiteSpace(voice.Culture));
    }

    [WindowsFact]
    public async Task Default_voice_is_female_when_available()
    {
        await using var service = new WindowsTextToSpeechService(Logger);

        var defaultVoice = service.GetDefaultVoice();

        defaultVoice?.Gender.Should().Be(SpeechVoiceGender.Female);
    }

    [WindowsFact]
    public async Task Enumerated_output_devices_have_stable_endpoint_ids_and_names()
    {
        await using var service = new WindowsTextToSpeechService(Logger);

        var devices = service.GetOutputDevices();
        var defaultDevice = service.GetDefaultOutputDevice();

        devices.Should().OnlyContain(device =>
            !string.IsNullOrWhiteSpace(device.Id) &&
            !string.IsNullOrWhiteSpace(device.Name));
        if (defaultDevice is not null)
        {
            devices.Should().Contain(device => device.Id == defaultDevice.Id);
        }
    }

    [WindowsFact]
    public async Task SpeakAsync_rejects_an_output_endpoint_that_does_not_exist()
    {
        await using var service = new WindowsTextToSpeechService(Logger);
        var voice = new SpeechVoice("test", "Test", "en-US", SpeechVoiceGender.Female);
        var missingOutput = new AudioOutputDevice(
            $"missing-{Guid.NewGuid():N}",
            "Missing output");

        var action = () => service.SpeakAsync("Hello", voice, missingOutput);

        await action.Should().ThrowAsync<AudioOutputDeviceUnavailableException>();
        service.IsSpeaking.Should().BeFalse();
    }
}