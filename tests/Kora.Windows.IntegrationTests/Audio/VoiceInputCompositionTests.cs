using AwesomeAssertions;
using Kora.Application.Voice;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Microsoft.Extensions.DependencyInjection;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class VoiceInputCompositionTests
{
    [Fact]
    public async Task Production_aliases_share_one_lifecycle_and_one_borrowed_native_adapter_without_opening_audio()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        VoiceInputComposition.Configure(services);
        services.Count(descriptor => descriptor.ServiceType == typeof(WindowsVoiceRecognitionService)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(HandsFreeVoiceRecognitionService)).Should().Be(1);
        await using var provider = services.BuildServiceProvider();
        var native = provider.GetRequiredService<WindowsVoiceRecognitionService>();
        var lifecycle = provider.GetRequiredService<HandsFreeVoiceRecognitionService>();
        provider.GetRequiredService<IVoiceRecognitionService>().Should().BeSameAs(lifecycle);
        provider.GetRequiredService<IActivatedVoiceRecognitionService>().Should().BeSameAs(lifecycle);
        provider.GetRequiredService<ISpeechInputProfileRuntime>().Should().BeSameAs(lifecycle);
        provider.GetRequiredService<IHandsFreeAudioStage>().Should().BeOfType<WindowsHandsFreeAudioStage>();
        lifecycle.IsAmbientListeningAvailable.Should().BeFalse();
        await lifecycle.StartHandsFreeAsync(cancellationToken: TestContext.Current.CancellationToken);
        lifecycle.IsListening.Should().BeFalse();
        native.IsCaptureQuiescent.Should().BeTrue();
        lifecycle.InitializeProfile(SpeechInputSelection.Default);
        await lifecycle.DisposeAsync();
        native.IsProfileAvailable.Should().BeTrue();
        native.IsCaptureQuiescent.Should().BeTrue();
        var cancelledNativeOpen = () => native.BeginPushToTalkAsync(new("synthetic", "Synthetic"),
            ["help"], cancellationToken: new CancellationToken(canceled: true));
        await cancelledNativeOpen.Should().ThrowExactlyAsync<OperationCanceledException>();
        await provider.DisposeAsync();
        await cancelledNativeOpen.Should().ThrowAsync<ObjectDisposedException>();
    }
}
