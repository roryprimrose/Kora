using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Context;

public static class LocalFilePolicy
{
    public const int MaximumBytes = 256 * 1024;
    public const int MaximumPathCharacters = 240;
    public const int MaximumDepth = 32;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly HashSet<string> ExcludedComponents = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".ssh", ".aws", ".azure", ".kube", ".config",
        "node_modules", "bin", "obj", ".vs", ".vscode", "credentials", "secrets",
    };

    public static void ValidatePath(string path) => ValidatePath(path, file: true);

    public static void ValidateFolderPath(string path) => ValidatePath(path, file: false);

    private static void ValidatePath(string path, bool file)
    {
        ArgumentNullException.ThrowIfNull(path);
        try { _ = Utf8.GetByteCount(path); }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidDataException("The path is not canonical Unicode.", exception);
        }
        if (path.Length is < 4 or > MaximumPathCharacters
            || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\'
            || path.AsSpan(3).ContainsAny('/', ':', '%')
            || path.Any(character => char.IsControl(character) || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format
                || character is '"' or '<' or '>' or '|' or '?' or '*'))
        {
            throw new InvalidDataException("Only bounded absolute fixed-drive paths are supported; no expansion, devices, streams or network paths.");
        }
        var components = path[3..].Split('\\');
        if (components.Length > MaximumDepth || components.Any(component => component.Length == 0
            || component is "." or ".." || component.EndsWith(' ') || component.EndsWith('.')
            || ExcludedComponents.Contains(component) || IsDeviceName(component)))
        {
            throw new InvalidDataException("The path is noncanonical, protected, generated, source-control metadata or too deep.");
        }
        var extension = components[^1].LastIndexOf('.');
        if (file && (extension < 0 || components[^1][extension..].ToLowerInvariant() is not (".txt" or ".md" or ".markdown")))
        {
            throw new InvalidDataException("Only UTF-8 plain-text and Markdown file extensions are supported.");
        }
    }

    private static bool IsDeviceName(string component)
    {
        var stem = component.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (stem.Length == 4 && stem[3] is >= '1' and <= '9'
                && stem[..3] is "COM" or "LPT");
    }

    public static bool IsWithin(string path, string root) =>
        string.Equals(path.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumBytes) { throw new InvalidDataException("The file exceeds the byte bound; no truncation is permitted."); }
        var payload = bytes.StartsWith((ReadOnlySpan<byte>)[0xef, 0xbb, 0xbf]) ? bytes[3..] : bytes;
        var text = Utf8.GetString(payload);
        if (text.Contains('\0', StringComparison.Ordinal)
            || text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
        {
            throw new InvalidDataException("Binary or unsupported control characters are not plain text.");
        }
        return text;
    }

    public static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
