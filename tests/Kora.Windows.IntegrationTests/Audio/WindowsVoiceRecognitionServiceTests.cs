using AwesomeAssertions;

using Kora.Core.Voice;
using Kora.Windows.Audio;

using Neovolve.Logging.Xunit;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class WindowsVoiceRecognitionServiceTests(
    ITestOutputHelper output) : LoggingTestsBase<WindowsVoiceRecognitionService>(output)
{
    [Fact]
    public async Task StopAsync_is_idempotent_before_capture_starts()
    {
        await using var service = new WindowsVoiceRecognitionService(Logger);

        await service.StopAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        service.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task StartAsync_rejects_null_arguments_before_accessing_hardware()
    {
        await using var service = new WindowsVoiceRecognitionService(Logger);
        var microphone = new MicrophoneDevice("0", "test");

        var nullMicrophone = () => service.StartAsync(null!, ["help"]);
        var nullPhrases = () => service.StartAsync(microphone, null!);

        await nullMicrophone.Should().ThrowAsync<ArgumentNullException>();
        await nullPhrases.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Disposed_service_rejects_capture_start()
    {
        var service = new WindowsVoiceRecognitionService(Logger);
        await service.DisposeAsync();

        var action = () => service.StartAsync(new MicrophoneDevice("0", "test"), ["help"]);

        await action.Should().ThrowAsync<ObjectDisposedException>();
    }

    [WindowsFact]
    public async Task Enumerated_microphones_have_stable_endpoint_ids_and_include_the_Windows_default()
    {
        await using var service = new WindowsVoiceRecognitionService(Logger);

        var microphones = service.GetMicrophones();
        var defaultMicrophone = service.GetDefaultMicrophone();

        microphones.Should().OnlyContain(device =>
            !string.IsNullOrWhiteSpace(device.Id) &&
            !string.IsNullOrWhiteSpace(device.Name));
        if (defaultMicrophone is not null)
        {
            microphones.Should().Contain(device => device.Id == defaultMicrophone.Id);
        }
    }

    [WindowsFact]
    public async Task StartAsync_rejects_a_device_that_is_not_present()
    {
        await using var service = new WindowsVoiceRecognitionService(Logger);

        var action = () => service.StartAsync(
            new MicrophoneDevice($"missing-{Guid.NewGuid():N}", "missing"),
            ["help"]);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
        service.IsListening.Should().BeFalse();
    }
}