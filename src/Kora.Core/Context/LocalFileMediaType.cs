namespace Kora.Core.Context;

/// <summary>The exact canonical media types admitted by retained text attachments.</summary>
public static class LocalFileMediaType
{
    public const string PlainText = "text/plain";
    public const string Markdown = "text/markdown";

    public static bool IsCanonical(string? value) => value is PlainText or Markdown;

    public static void Validate(string value)
    {
        if (!IsCanonical(value)) { throw new InvalidDataException("Unknown or noncanonical attachment media type."); }
    }
}
