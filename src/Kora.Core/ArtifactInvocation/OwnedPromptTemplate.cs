using System.Buffers;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Artifacts;

/// <summary>Native v2 literal user-request parameters, never expressions or authority.</summary>
public sealed class OwnedPromptTemplate
{
    public const int MaximumFields = 8;
    public const int MaximumNameLength = 64;
    public const int MaximumValueLength = 512;
    public const int MaximumValueUtf8Bytes = 2048;
    public const int MaximumRequestLength = 4096;
    public const int MaximumRequestUtf8Bytes = 16384;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly SearchValues<char> NameCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789-");
    private readonly ImmutableArray<Substitution> substitutions;
    private readonly string body;

    private OwnedPromptTemplate(string body, ImmutableArray<string> names, ImmutableArray<Substitution> substitutions)
    {
        this.body = body;
        RequiredNames = names;
        this.substitutions = substitutions;
    }

    public ImmutableArray<string> RequiredNames { get; }

    public static OwnedPromptTemplate Parse(string body)
    {
        ValidateUnicode(body, ArtifactDefinition.MaximumContentLength, ArtifactDefinition.MaximumContentLength);
        if (string.IsNullOrWhiteSpace(body)) { throw new InvalidDataException("A prompt requires a user request."); }
        var names = new List<string>();
        var markers = ImmutableArray.CreateBuilder<Substitution>();
        var offset = 0;
        while ((offset = body.IndexOf("${", offset, StringComparison.Ordinal)) >= 0)
        {
            var end = body.IndexOf('}', offset + 2);
            if (end < 0 || offset > 0 && body[offset - 1] is '\\' or '$')
            { throw new InvalidDataException("Unclosed or escaped prompt substitution is unsupported."); }
            var marker = body.AsSpan(offset + 2, end - offset - 2);
            if (!marker.StartsWith("input:", StringComparison.Ordinal))
            { throw new InvalidDataException("Only required literal ${input:name} prompt parameters are supported."); }
            var name = marker[6..];
            if (name.Length is 0 or > MaximumNameLength || name[0] is not (>= 'a' and <= 'z')
                || name.ContainsAnyExcept(NameCharacters))
            { throw new InvalidDataException("Prompt parameter names require 1–64 lowercase ASCII letters, digits or hyphens, starting with a letter."); }
            var text = name.ToString();
            var index = names.IndexOf(text);
            if (index < 0)
            {
                if (names.Count == MaximumFields) { throw new InvalidDataException("A prompt supports at most eight distinct required inputs."); }
                index = names.Count;
                names.Add(text);
            }
            markers.Add(new(offset, end + 1 - offset, index));
            offset = end + 1;
        }
        return new(body, names.ToImmutableArray(), markers.ToImmutable());
    }

    public string Expand(IReadOnlyDictionary<string, string> values, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(values);
        token.ThrowIfCancellationRequested();
        if (values.Count != RequiredNames.Length || values.Keys.Any(name => !RequiredNames.Contains(name, StringComparer.Ordinal)))
        { throw new InvalidDataException("Supply exactly the declared required prompt inputs; no extra inputs are accepted."); }
        foreach (var name in RequiredNames)
        {
            if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            { throw new InvalidDataException("Every declared prompt input requires nonblank native text."); }
            ValidateValue(value);
        }
        var length = body.Length;
        foreach (var substitution in substitutions)
        {
            token.ThrowIfCancellationRequested();
            length += values[RequiredNames[substitution.Index]].Length - substitution.Length;
        }
        if (length > MaximumRequestLength)
        { throw new InvalidDataException("The complete expanded user request exceeds 4096 UTF-16 code units."); }
        var expanded = new StringBuilder(length);
        var offset = 0;
        foreach (var substitution in substitutions)
        {
            token.ThrowIfCancellationRequested();
            expanded.Append(body, offset, substitution.Offset - offset);
            // Only original-template markers are visited. Values containing markers stay literal.
            expanded.Append(values[RequiredNames[substitution.Index]]);
            offset = substitution.Offset + substitution.Length;
        }
        expanded.Append(body, offset, body.Length - offset);
        var request = expanded.ToString();
        ValidateUnicode(request, MaximumRequestLength, MaximumRequestUtf8Bytes);
        return request;
    }

    public static void ValidateValue(string value) => ValidateUnicode(value, MaximumValueLength, MaximumValueUtf8Bytes);

    public static string RequestDigest(string request) =>
        Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(request)));

    private static void ValidateUnicode(string text, int maximumLength, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > maximumLength || text.Contains('\0', StringComparison.Ordinal))
        { throw new InvalidDataException("Prompt text exceeds its UTF-16 limit or contains NUL."); }
        try
        {
            if (Utf8.GetByteCount(text) > maximumBytes)
            { throw new InvalidDataException("Prompt text exceeds its strict UTF-8 byte limit."); }
        }
        catch (EncoderFallbackException exception)
        { throw new InvalidDataException("Prompt text must be well-formed Unicode.", exception); }
    }

    private sealed record Substitution(int Offset, int Length, int Index);
}
