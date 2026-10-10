using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kora.Core.Network;

public sealed class PreapprovedUriPattern
{
    public const int MaximumPatternCount = 64;
    public const int MaximumPatternUtf8Bytes = 2048;
    public const int MaximumTotalUtf8Bytes = 16384;

    private readonly Regex matcher;

    private PreapprovedUriPattern(string value, Regex matcher)
    {
        Value = value;
        this.matcher = matcher;
    }

    public string Value { get; }

    public static PreapprovedUriPattern Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(char.IsControl)
            || Encoding.UTF8.GetByteCount(value) > MaximumPatternUtf8Bytes)
        {
            throw new ArgumentException(
                "A preapproved URI pattern must be non-empty, unpadded, control-free, and at most 2048 UTF-8 bytes.",
                nameof(value));
        }

        var schemeSeparator = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator < 0)
        {
            throw new ArgumentException("A preapproved URI pattern must be an absolute HTTP or HTTPS URI.", nameof(value));
        }

        var scheme = value[..schemeSeparator].ToLowerInvariant();
        if (scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Only HTTP and HTTPS URI patterns can be preapproved.", nameof(value));
        }

        var remainder = value[(schemeSeparator + 3)..];
        var resourceStart = remainder.IndexOfAny(['/', '?', '#']);
        var authority = resourceStart < 0 ? remainder : remainder[..resourceStart];
        var resource = resourceStart < 0 ? "/" : remainder[resourceStart..];
        if (authority.Length == 0 || authority.Contains('@', StringComparison.Ordinal)
            || resource.Contains('#', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A preapproved URI pattern requires a host and cannot contain credentials or a fragment.",
                nameof(value));
        }

        var (host, port) = ParseAuthority(authority, scheme, value);
        var labels = host.Split('.');
        if (labels.All(label => string.Equals(label, "*", StringComparison.Ordinal))
            || labels.Any(label => label.Length == 0
            || label.Contains('*') && !string.Equals(label, "*", StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "A host wildcard must occupy an entire DNS label and the host must include a literal label, for example https://*.example.com/.",
                nameof(value));
        }

        var canonicalHost = string.Join(".", labels.Select(label =>
            string.Equals(label, "*", StringComparison.Ordinal)
                ? label
                : new IdnMapping().GetAscii(label).ToLowerInvariant()));
        var canonicalResource = CanonicalizeResource(resource);
        var canonical = scheme + "://" + canonicalHost + port + canonicalResource;
        var hostExpression = Regex.Escape(scheme + "://" + canonicalHost + port)
            .Replace(@"\*", "[^./]+", StringComparison.Ordinal);
        var resourceExpression = Regex.Escape(canonicalResource)
            .Replace(@"\*", ".*", StringComparison.Ordinal);
        var expression = "^" + hostExpression + resourceExpression + "$";
        return new(canonical, new Regex(
            expression,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking,
            TimeSpan.FromMilliseconds(100)));
    }

    public bool Matches(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return matcher.IsMatch(uri.AbsoluteUri);
    }

    public override string ToString() => Value;

    private static (string Host, string Port) ParseAuthority(string authority, string scheme, string original)
    {
        if (!Uri.TryCreate($"{scheme}://{authority}/", UriKind.Absolute, out var parsed)
            || parsed.HostNameType is UriHostNameType.IPv6
            || parsed.Host.Contains('*', StringComparison.Ordinal))
        {
            var placeholder = authority.Replace("*", "wildcard", StringComparison.Ordinal);
            if (!Uri.TryCreate($"{scheme}://{placeholder}/", UriKind.Absolute, out parsed)
                || parsed.HostNameType is UriHostNameType.IPv6)
            {
                throw new ArgumentException("The preapproved URI authority is invalid.", nameof(original));
            }
        }

        var colon = authority.LastIndexOf(':');
        var hasPort = colon > -1;
        var host = hasPort ? authority[..colon] : authority;
        var port = hasPort ? authority[(colon + 1)..] : string.Empty;
        if (hasPort && (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < 1))
        {
            throw new ArgumentException("The preapproved URI port is invalid.", nameof(original));
        }

        var isDefaultPort = string.Equals(scheme, "http", StringComparison.Ordinal)
            && string.Equals(port, "80", StringComparison.Ordinal)
            || string.Equals(scheme, "https", StringComparison.Ordinal)
            && string.Equals(port, "443", StringComparison.Ordinal);
        var normalizedPort = hasPort && !isDefaultPort ? ":" + port : string.Empty;
        return (host, normalizedPort);
    }

    private static string CanonicalizeResource(string resource)
    {
        if (resource[0] == '?')
        {
            resource = "/" + resource;
        }
        var tokenNumber = 0;
        string token;
        do
        {
            token = $"kora-uri-wildcard-{tokenNumber++}";
        }
        while (resource.Contains(token, StringComparison.Ordinal));
        var placeholder = resource.Replace("*", token, StringComparison.Ordinal);
        var parsed = new Uri("https://example.invalid" + placeholder, UriKind.Absolute);
        return parsed.PathAndQuery.Replace(token, "*", StringComparison.Ordinal);
    }
}
