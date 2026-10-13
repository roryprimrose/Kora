using System.Security.Cryptography;

namespace Kora.Core.Artifacts;

public static class ArtifactSourceComparison
{
    public static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string ChangedFields(ArtifactFileDocument? original, ArtifactFileDocument? proposed)
    {
        if (original is null) { return "new file (all fields and body)"; }
        if (proposed is null) { return "delete entire original"; }
        var changed = original.Metadata.Keys.Union(proposed.Metadata.Keys, StringComparer.Ordinal)
            .Where(key => !string.Equals(original.Metadata.GetValueOrDefault(key), proposed.Metadata.GetValueOrDefault(key), StringComparison.Ordinal)).ToList();
        if (!string.Equals(original.Definition.Content, proposed.Definition.Content, StringComparison.Ordinal)) { changed.Add("body"); }
        if (!string.Equals(original.Definition.Digest, proposed.Definition.Digest, StringComparison.Ordinal)) { changed.Add("exact source bytes/formatting"); }
        return changed.Count == 0 ? "(unchanged bytes)" : string.Join(", ", changed);
    }
}
