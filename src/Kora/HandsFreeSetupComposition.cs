using Kora.Core.Dependencies;
using Kora.Windows.Audio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kora;

internal static class HandsFreeSetupComposition
{
    internal static void Configure(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton(HandsFreeSetupCatalogue.Production));
        services.AddSingleton<HandsFreeSetupRuntime>();
        services.Replace(ServiceDescriptor.Singleton<ISpeechComponentRuntime>(provider =>
            provider.GetRequiredService<HandsFreeSetupRuntime>()));
    }
}
