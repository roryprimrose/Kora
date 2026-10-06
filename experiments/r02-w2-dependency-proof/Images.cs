using System.Diagnostics;
using System.Security.Cryptography;

namespace W2Proof;

internal sealed record ImageIdentity(string Path, string? Sha256, string? Error);

internal static class Images
{
    internal static ImageIdentity[] Capture()
    {
        using var process = Process.GetCurrentProcess();
        return process.Modules.Cast<ProcessModule>().Select(module =>
        {
            string path = module.FileName;
            try
            {
                using var stream = File.OpenRead(path);
                return new ImageIdentity(path, Convert.ToHexStringLower(SHA256.HashData(stream)), null);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return new ImageIdentity(path, null, $"Unknown identity: {error.GetType().Name} 0x{error.HResult:x8}");
            }
        }).OrderBy(image => image.Path, StringComparer.Ordinal).ToArray();
    }
}
