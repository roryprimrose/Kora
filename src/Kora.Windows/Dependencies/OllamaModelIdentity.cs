namespace Kora.Windows.Dependencies;

internal static class OllamaModelIdentity
{
    private const string Sha256Prefix = "sha256:";

    public static bool HasPinnedDigest(string? reportedDigest)
    {
        // The tags API returns bare hex; registry-style digests include the algorithm.
        var digest = reportedDigest.AsSpan();
        if (digest.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            digest = digest[Sha256Prefix.Length..];
        }
        if (digest.Length != 64)
        {
            return false;
        }
        foreach (var character in digest)
        {
            if (!char.IsAsciiHexDigit(character)) { return false; }
        }
        return digest.Equals(WindowsOllamaSetupService.ModelDigest.AsSpan(Sha256Prefix.Length),
            StringComparison.OrdinalIgnoreCase);
    }
}
