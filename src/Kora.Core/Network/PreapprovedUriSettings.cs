using System.Text;

namespace Kora.Core.Network;

public sealed record PreapprovedUriSettings(IReadOnlyList<string> Patterns)
{
    public static PreapprovedUriSettings Empty { get; } = new([]);

    public static PreapprovedUriSettings Create(IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        var parsed = patterns.Select(PreapprovedUriPattern.Parse).ToArray();
        if (parsed.Length > PreapprovedUriPattern.MaximumPatternCount)
        {
            throw new ArgumentException(
                $"At most {PreapprovedUriPattern.MaximumPatternCount} URI patterns can be preapproved.",
                nameof(patterns));
        }
        if (parsed.Select(item => item.Value).Distinct(StringComparer.Ordinal).Count() != parsed.Length)
        {
            throw new ArgumentException("Preapproved URI patterns must be unique.", nameof(patterns));
        }
        if (parsed.Sum(item => Encoding.UTF8.GetByteCount(item.Value)) > PreapprovedUriPattern.MaximumTotalUtf8Bytes)
        {
            throw new ArgumentException("The preapproved URI patterns exceed the total 16384 UTF-8 byte limit.", nameof(patterns));
        }
        return new(Array.AsReadOnly(parsed.Select(item => item.Value).Order(StringComparer.Ordinal).ToArray()));
    }

    public bool IsPreapproved(Uri uri) =>
        Patterns.Select(PreapprovedUriPattern.Parse).Any(pattern => pattern.Matches(uri));
}
