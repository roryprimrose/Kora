using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using GitHub.Copilot;

namespace Kora.Rt1;

internal static class Candidate
{
    internal const string SourceCommit = "f8ae645902b74b62cd47aac1fd9b29adaec3aff2";
    internal const string SdkVersion = "1.0.16";
    internal const string Credential = "RT1_SYNTHETIC_NOT_A_CREDENTIAL";
    internal static string Root { get; } = SourceDirectory();
    internal static string RuntimeDirectory => Path.Combine(Root, ".inputs", "runtime");
    internal static string Executable => Path.Combine(RuntimeDirectory, "copilot-runtime.exe");
    private static string SourceDirectory([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;
    internal static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }
    internal static async Task ValidateAsync()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64
            || RuntimeInformation.FrameworkDescription != ".NET 10.0.12")
            throw new PlatformNotSupportedException("Released MG1 profile requires Windows x64/.NET 10.0.12.");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "copilot-runtime.exe", "runtime.node", "LICENSE.md" };
        if (Directory.EnumerateFileSystemEntries(RuntimeDirectory).Any(path => !allowed.Contains(Path.GetFileName(path))))
            throw new InvalidDataException("Unexpected active runtime asset.");
        if (await HashAsync(Executable) != "7021cf1f25eb6b75e64c05e8f805747c62dd420dc8760659660291809e92603a"
            || await HashAsync(Path.Combine(RuntimeDirectory, "runtime.node")) != "41ebb48367f96c984babde61afb68f22a85ab8fd9c037fdccf1778881c4bba05"
            || await HashAsync(typeof(CopilotClient).Assembly.Location) != "6ed0b19fd2f9cf525074830784bb255245f15b8f08a3d6aa78fb116be5ba668b"
            || await HashAsync(Path.Combine(Root, ".inputs", "GitHub.Copilot.SDK.1.0.16.nupkg")) != "c5518980b71d0ef0abd39ecdec7834898878222290c7fd1099a9040c8c5bf2ef"
            || typeof(CopilotClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                != SdkVersion + "+" + SourceCommit)
            throw new InvalidDataException("Released SDK/native hashes or provenance differ from the separately approved profile.");
    }
}
