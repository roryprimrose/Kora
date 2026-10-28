using Kora.Application.Configuration;
using Kora.Core.Skills;
using Kora.Windows.Skills.RuntimeSetup;

using Microsoft.Extensions.DependencyInjection;

namespace Kora;

internal static class BundledRuntimeSetupComposition
{
    internal static void Configure(IServiceCollection services)
    {
        services.AddSingleton<IBundledRuntimeRegistrationPreferences, LocalBundledRuntimeRegistrationPreferences>();
        services.AddSingleton<WindowsBundledRuntimeSetup>();
        services.AddSingleton<IBundledScriptWorker>(provider => provider.GetRequiredService<WindowsBundledRuntimeSetup>().CreateWorker());
    }
}
