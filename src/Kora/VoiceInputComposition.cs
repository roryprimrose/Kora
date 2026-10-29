using Kora.Application.Voice;
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
        services.AddSingleton<WindowsVoiceRecognitionService>();
        services.AddSingleton<IHandsFreeAudioStage, WindowsHandsFreeAudioStage>();
        services.AddSingleton<HandsFreeVoiceRecognitionService>(provider => new(
            provider.GetRequiredService<WindowsVoiceRecognitionService>(),
            provider.GetRequiredService<IHandsFreeAudioStage>(),
            provider.GetRequiredService<ILogger<HandsFreeVoiceRecognitionService>>()));
        services.AddSingleton<ISpeechInputProfileRuntime>(provider =>
            provider.GetRequiredService<HandsFreeVoiceRecognitionService>());
        services.AddSingleton<IVoiceRecognitionService>(provider =>
            provider.GetRequiredService<HandsFreeVoiceRecognitionService>());
        services.AddSingleton<IActivatedVoiceRecognitionService>(provider =>
            provider.GetRequiredService<HandsFreeVoiceRecognitionService>());
    }
}
