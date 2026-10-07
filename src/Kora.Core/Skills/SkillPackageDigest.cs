using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Skills;

public static class SkillPackageDigest
{
    public const string ScriptSetVersion = "Kora.ScriptSet.v1";
    public const string DefinitionVersion = "Kora.SkillDefinition.v1";
    public const string DeclaredResourcesVersion = "Kora.DeclaredResources.v1";
    public const int MaximumFiles = 16;
    public const int MaximumTotalBytes = 256 * 1024;

    public static string Compute(string version, IReadOnlyList<SkillResourceSnapshot> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (version is not (ScriptSetVersion or DefinitionVersion or DeclaredResourcesVersion))
        {
            throw new InvalidDataException("Unsupported skill digest encoding.");
        }
        if (files.Count is 0 or > MaximumFiles || files.Any(file => file is null)
            || files.Sum(file => (long)file.Bytes.Length) > MaximumTotalBytes
            || files.Select(file => file.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count
            || files.Select(file => file.ResourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
        {
            throw new InvalidDataException("The complete resource set exceeds its bounds or contains aliases.");
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes(version));
        hash.AppendData([0]);
        Span<byte> framing = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(framing, (uint)files.Count);
        hash.AppendData(framing[..4]);
        foreach (var file in files.OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            var name = Encoding.UTF8.GetBytes(file.Name);
            BinaryPrimitives.WriteUInt32BigEndian(framing, (uint)name.Length);
            hash.AppendData(framing[..4]);
            hash.AppendData(name);
            if (version is DeclaredResourcesVersion)
            {
                var resourceId = Encoding.UTF8.GetBytes(file.ResourceId);
                BinaryPrimitives.WriteUInt32BigEndian(framing, (uint)resourceId.Length);
                hash.AppendData(framing[..4]);
                hash.AppendData(resourceId);
            }
            BinaryPrimitives.WriteUInt64BigEndian(framing, (ulong)file.Bytes.Length);
            hash.AppendData(framing);
            hash.AppendData(file.Bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
