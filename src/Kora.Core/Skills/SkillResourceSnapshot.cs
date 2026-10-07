using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Skills;

public sealed class SkillResourceSnapshot
{
    public const int MaximumBytes = 64 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly byte[] bytes;

    public SkillResourceSnapshot(string name, string resourceId, ReadOnlySpan<byte> content)
    {
        Name = ValidateName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        if (resourceId.Length > 128 || resourceId.Any(c =>
            c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.')))
        {
            throw new InvalidDataException("An explicit bounded ASCII resource ID is required.");
        }
        if (content.Length is 0 or > MaximumBytes)
        {
            throw new InvalidDataException("A skill resource must contain between 1 and 65536 bytes.");
        }
        bytes = content.ToArray();
        try
        {
            Text = Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Skill resources require strict UTF-8, optionally with a UTF-8 BOM.", exception);
        }
        if (Text.Contains('\0', StringComparison.Ordinal))
        {
            throw new InvalidDataException("NUL and UTF-16 skill resources are not supported.");
        }
        ResourceId = resourceId;
        Digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public string Name { get; }
    public string ResourceId { get; }
    public string Text { get; }
    public string Digest { get; }
    public ReadOnlySpan<byte> Bytes => bytes;

    public static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 256 || name.Split('\\').Any(segment =>
            segment.Length == 0 || segment is "." or ".."
            || segment.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_'))))
        {
            throw new InvalidDataException("Logical names require bounded lowercase ASCII segments and backslash separators; aliases are rejected.");
        }
        return name;
    }
}
