using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Kora.Application;

public sealed class AssemblyApplicationInfo(Func<Version?>? versionProvider = null) : IApplicationInfo
{
    private readonly Func<Version?> versionProvider = versionProvider ?? GetEntryAssemblyVersion;

    public string Version => versionProvider()?.ToString(3) ?? "development";

    [ExcludeFromCodeCoverage(
        Justification = "The null entry-assembly path is controlled by the unmanaged process host; injected providers cover version behavior.")]
    private static Version? GetEntryAssemblyVersion() => Assembly.GetEntryAssembly()?.GetName().Version;
}