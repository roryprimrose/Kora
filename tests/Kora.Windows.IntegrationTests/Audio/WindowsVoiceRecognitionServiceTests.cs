using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Voice;
using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class WindowsVoiceRecognitionServiceTests
{
    [Fact]
    public async Task StopAsync_is_idempotent_before_capture_starts()
    {
        await using var service = new WindowsVoiceRecognitionService();

        await service.StopAsync();
        await service.StopAsync();

        service.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_rejects_null_arguments_before_accessing_hardware()
    {
        await using var service = new WindowsVoiceRecognitionService();
        var microphone = new MicrophoneDevice("0", "test");

        var nullMicrophone = () => service.StartAsync(null!, ["help"]);
        var nullPhrases = () => service.StartAsync(microphone, null!);

        await nullMicrophone.Should().ThrowAsync<ArgumentNullException>();
        await nullPhrases.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Disposed_service_rejects_capture_start()
    {
        var service = new WindowsVoiceRecognitionService();
        await service.DisposeAsync();

        var action = () => service.StartAsync(new MicrophoneDevice("0", "test"), ["help"]);

        await action.Should().ThrowAsync<ObjectDisposedException>();
    }

    [WindowsFact]
    public async Task Enumerated_microphones_have_stable_numeric_ids_and_names()
    {
        await using var service = new WindowsVoiceRecognitionService();

        var microphones = service.GetMicrophones();

        microphones.Should().OnlyContain(device =>
            device.Id.Length > 0 &&
            device.Id.All(char.IsDigit) &&
            !string.IsNullOrWhiteSpace(device.Name));
    }

    [WindowsFact]
    public async Task StartAsync_rejects_a_device_that_is_not_present()
    {
        await using var service = new WindowsVoiceRecognitionService();

        var action = () => service.StartAsync(
            new MicrophoneDevice(int.MaxValue.ToString(CultureInfo.InvariantCulture), "missing"),
            ["help"]);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
        service.IsListening.Should().BeFalse();
    }
}