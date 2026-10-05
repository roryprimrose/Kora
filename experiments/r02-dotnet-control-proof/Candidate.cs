using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Reflection;
using GitHub.Copilot;

namespace Kora.Rt1;

internal static class Candidate
{
    internal const string SourceCommit = "f8ae645902b74b62cd47aac1fd9b29adaec3aff2";
    internal const string SdkVersion = "1.0.16-rt1.source.f8ae645.1";
    internal const string Credential = "RT1_SYNTHETIC_NOT_A_CREDENTIAL";
    internal const string Denied = "RT1_DENIED_MARKER";
    internal const string Collected = "RT1_DENIED_DISCOVERY";
    internal static string Root { get; } = SourceDirectory();
    internal static string RuntimeDirectory => Path.Combine(Root, ".candidate", "runtime");
    internal static string Executable => Path.Combine(RuntimeDirectory, "copilot-runtime.exe");

    private static string SourceDirectory([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

    internal static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }

    internal static async Task ValidateAsync()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("RT1 requires Windows x64.");
        }
        if (RuntimeInformation.FrameworkDescription != ".NET 10.0.12")
            throw new InvalidDataException("RT1 requires the reviewed .NET runtime 10.0.12.");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "copilot-runtime.exe", "runtime.node", "LICENSE.md"
        };
        if (Directory.EnumerateFileSystemEntries(RuntimeDirectory).Any(path => !allowed.Contains(Path.GetFileName(path))))
            throw new InvalidDataException("Unexpected asset in minimal active runtime directory.");
        if (await HashAsync(Executable) != "7021cf1f25eb6b75e64c05e8f805747c62dd420dc8760659660291809e92603a"
            || await HashAsync(Path.Combine(RuntimeDirectory, "runtime.node"))
                != "41ebb48367f96c984babde61afb68f22a85ab8fd9c037fdccf1778881c4bba05")
        {
            throw new InvalidDataException("RT1 native bytes differ from the reviewed candidate.");
        }
        if (!typeof(CopilotClient).Assembly.GetCustomAttributes<AssemblyInformationalVersionAttribute>()
                .Any(version => version.InformationalVersion.StartsWith(SdkVersion, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("RT1 must use the distinctly labelled exact-tag source build.");
        }
        if (await HashAsync(typeof(CopilotClient).Assembly.Location)
                != "afb0715225f794d1b663ef72da1336b44b665e50b330087e1356380a5eac10d6"
            || await HashAsync(Path.Combine(Root, ".candidate", "feed", "GitHub.Copilot.SDK." + SdkVersion + ".nupkg"))
                != "0b609b73c868099d64e7d5320b927d760d528756ab7c5db0b5692f5dccbf9b3f")
        {
            throw new InvalidDataException("RT1 SDK bytes differ from the reviewed reproducible source build.");
        }
    }
}
