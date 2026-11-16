using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Presentation;

/// <summary>One exact file in a <see cref="ReviewContents"/> set, addressed by a relative forward-slash path.</summary>
public sealed class ReviewDocument
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    private ReviewDocument(string path, string text, string digest)
    {
        Path = path;
        Text = text;
        Digest = digest;
    }

    public string Path { get; }
    public string Text { get; }

    /// <summary>Lowercase SHA-256 of the exact bytes the item uses.</summary>
    public string Digest { get; }

    public ImmutableArray<string> Segments => [.. Path.Split('/')];

    public static ReviewDocument FromBytes(string path, ReadOnlySpan<byte> bytes) =>
        new(NormalizePath(path), StrictUtf8.GetString(bytes), Convert.ToHexStringLower(SHA256.HashData(bytes)));

    public static ReviewDocument FromText(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return FromBytes(path, Encoding.UTF8.GetBytes(text));
    }

    internal static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || System.IO.Path.IsPathRooted(path) || path.Contains(':', StringComparison.Ordinal)
            || segments.Any(segment => segment is "." or ".." || segment.Any(char.IsControl)))
        {
            throw new ArgumentException("Review document paths must be relative and contained.", nameof(path));
        }
        return string.Join('/', segments);
    }
}
