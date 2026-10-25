using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Customizations;

public sealed class CustomizationFile
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly byte[] bytes;
    public CustomizationFile(string name, ReadOnlySpan<byte> bytes)
    {
        Name = ValidateName(name);
        if (bytes.Length > 64 * 1024) { throw new InvalidDataException("A component exceeds 64 KiB."); }
        HasBom = bytes.StartsWith(Encoding.UTF8.Preamble);
        try { Text = Utf8.GetString(HasBom ? bytes[Encoding.UTF8.Preamble.Length..] : bytes); }
        catch (DecoderFallbackException exception)
        { throw new InvalidDataException("A component is not strict UTF-8; it cannot be omitted or rewritten.", exception); }
        if (Text.Contains('\0', StringComparison.Ordinal))
        { throw new InvalidDataException("Binary components are unavailable in the text editor."); }
        this.bytes = bytes.ToArray();
    }

    public string Name { get; }
    public string Text { get; }
    public bool HasBom { get; }
    public byte[] CopyBytes() => bytes.ToArray();
    public string Digest => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public CustomizationFile WithText(string text) =>
        new(Name, Artifacts.ArtifactFileDocument.Encode(text, HasBom));

    public static string ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var parts = name.Split('\\');
        if (name.Length is 0 or > 200 || parts.Length > 4 || parts.Any(part =>
            part.Length is 0 or > 80 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
            || part.Any(character => char.IsControl(character) || character is '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
            || IsDeviceName(part)))
        { throw new InvalidDataException("Use a bounded relative component name without aliases, device names or traversal."); }
        return name;
    }

    private static bool IsDeviceName(string name)
    {
        var stem = name.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '0' and <= '9';
    }
}
