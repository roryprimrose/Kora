using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Kora.Application;

public sealed class AssemblyApplicationInfo(Func<string?>? versionProvider = null) : IApplicationInfo
{
    private readonly Func<string?> versionProvider = versionProvider ?? GetEntryAssemblyVersion;

    public string Version => versionProvider() ?? "development";

    [ExcludeFromCodeCoverage(
        Justification = "The null entry-assembly path is controlled by the unmanaged process host; injected providers cover version behavior.")]
    private static string? GetEntryAssemblyVersion() => Assembly.GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
}