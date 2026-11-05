using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Kora.Windows.Dependencies;
using Kora.Windows.IntegrationTests.Dependencies;
using Microsoft.Extensions.DependencyInjection;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class VoiceInputCompositionTests
{
    [Fact]
    public async Task Production_aliases_share_one_lifecycle_and_one_borrowed_native_adapter_without_opening_audio()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISpeechComponentFiles>(new InMemorySpeechComponentFiles());
        var store = new AudioControlTestStore();
        await using var admission = new AudioControlAdmission(store, store, new(store));
        var preferences = new OllamaAdmissionWorkflowTests.MemoryPreferences();
        var audit = new Audit();
        services.AddSingleton(admission);
        services.AddSingleton<IPreferenceStore>(preferences);
        services.AddSingleton<ISecurityAuditLog>(audit);
        VoiceInputComposition.Configure(services);
        services.Count(descriptor => descriptor.ServiceType == typeof(WindowsVoiceRecognitionService)).Should().Be(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(HandsFreeVoiceRecognitionService)).Should().Be(1);
        await using var provider = services.BuildServiceProvider();
        var timing = provider.GetRequiredService<VoiceCaptureTimingConfigurationService>();
        provider.GetRequiredService<IVoiceCaptureTimingSource>().Should().BeSameAs(timing);
        timing.GetTiming().Should().Be(VoiceCaptureTiming.Default);
        var native = provider.GetRequiredService<WindowsVoiceRecognitionService>();
        var lifecycle = provider.GetRequiredService<HandsFreeVoiceRecognitionService>();
        provider.GetRequiredService<IVoiceRecognitionService>().Should().BeSameAs(lifecycle);
        provider.GetRequiredService<IActivatedVoiceRecognitionService>().Should().BeSameAs(lifecycle);
        provider.GetRequiredService<ISpeechInputProfileRuntime>().Should().BeSameAs(lifecycle);
        provider.GetRequiredService<ISpeechSetupComparisonRuntime>().Should().BeSameAs(lifecycle);
        provider.GetRequiredService<SpeechSetupComparison>().Status.Should().Contain("No microphone");
        lifecycle.GetComparisonProfiles().Should().BeEmpty();
        provider.GetRequiredService<IHandsFreeAudioStage>().Should().BeOfType<WindowsHandsFreeAudioStage>();
        provider.GetRequiredService<Kora.Core.Dependencies.ISpeechComponentRuntime>()
            .Should().BeSameAs(provider.GetRequiredService<HandsFreeSetupRuntime>());
        provider.GetRequiredService<Kora.Core.Dependencies.SpeechComponentCatalogue>()
            .Should().BeSameAs(HandsFreeSetupCatalogue.Production);
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
        preferences.Values.Should().BeEmpty();
        audit.Events.Should().BeEmpty();
        store.Tasks.Should().BeEmpty();
    }

    private sealed class Audit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
