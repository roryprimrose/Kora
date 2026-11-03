using Kora.Application.Voice;
using Kora.Application.Configuration;
using Kora.Core.Voice;
using Kora.Windows.Audio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kora;

internal static class VoiceInputComposition
{
    internal static void Configure(IServiceCollection services)
    {
        HandsFreeSetupComposition.Configure(services);
        services.AddSingleton<IVoiceCaptureTimingPreferences>(provider =>
            new LocalVoiceCaptureTimingPreferences(provider.GetRequiredService<IPreferenceStore>()));
        services.AddSingleton<VoiceCaptureTimingConfigurationService>();
        services.AddSingleton<IVoiceCaptureTimingSource>(provider =>
            provider.GetRequiredService<VoiceCaptureTimingConfigurationService>());
        services.AddSingleton<WindowsVoiceRecognitionService>();
        services.AddSingleton<IHandsFreeAudioStage, WindowsHandsFreeAudioStage>();
        services.AddSingleton<HandsFreeVoiceRecognitionService>(provider => new(
            provider.GetRequiredService<WindowsVoiceRecognitionService>(),
            provider.GetRequiredService<IHandsFreeAudioStage>(),
            provider.GetRequiredService<ILogger<HandsFreeVoiceRecognitionService>>()));
        services.AddSingleton<ISpeechInputProfileRuntime>(provider =>
            provider.GetRequiredService<HandsFreeVoiceRecognitionService>());
        services.AddSingleton<ISpeechSetupComparisonRuntime>(provider =>
            provider.GetRequiredService<HandsFreeVoiceRecognitionService>());
        services.AddSingleton<SpeechSetupComparison>();
        services.AddSingleton<IVoiceRecognitionService>(provider =>
            provider.GetRequiredService<HandsFreeVoiceRecognitionService>());
        services.AddSingleton<IActivatedVoiceRecognitionService>(provider =>
            provider.GetRequiredService<HandsFreeVoiceRecognitionService>());
    }
}
