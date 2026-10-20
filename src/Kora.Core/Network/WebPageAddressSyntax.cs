using System.Globalization;
using System.Text;

namespace Kora.Core.Network;

/// <summary>Pure HTTP address grammar, not destination authorization or network screening.</summary>
public static class WebPageAddressSyntax
{
    public static bool IsHttpAddressWithoutCredentials(Uri address) =>
        address.IsAbsoluteUri && address.Scheme is "http" or "https"
        && string.IsNullOrEmpty(address.UserInfo);

    public static WebUrlSyntaxResult InspectExact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || text.Length > WebPageCapability.MaximumInputUtf8Bytes
            || Encoding.UTF8.GetByteCount(text) > WebPageCapability.MaximumInputUtf8Bytes)
        {
            return new(WebUrlSyntaxOutcome.OutsideBounds);
        }
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsWhiteSpace(character) || char.IsControl(character)
                || char.GetUnicodeCategory(character) == UnicodeCategory.Format
                || character == '\\' || char.IsSurrogate(character))
            {
                return new(WebUrlSyntaxOutcome.InvalidSyntax);
            }
            if (character == '%' && (index + 2 >= text.Length
                || !Uri.IsHexDigit(text[index + 1]) || !Uri.IsHexDigit(text[index + 2])))
            {
                return new(WebUrlSyntaxOutcome.InvalidSyntax);
            }
        }
        var authorityStart = text.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0) { return new(WebUrlSyntaxOutcome.InvalidSyntax); }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var address)
            || !IsHttpAddressWithoutCredentials(address)
            || !address.IsWellFormedOriginalString()
            || address.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
        {
            return new(WebUrlSyntaxOutcome.InvalidSyntax);
        }
        var authority = text.AsSpan(authorityStart + 3);
        var end = authority.IndexOfAny('/', '?', '#');
        if (end >= 0) { authority = authority[..end]; }
        // Empty user-info and scoped IPv6 identifiers must not become displayed host facts.
        if (authority.Contains('@') || authority.Contains('%') || authority.EndsWith(":", StringComparison.Ordinal))
        {
            return new(WebUrlSyntaxOutcome.InvalidSyntax);
        }
        return new(WebUrlSyntaxOutcome.Accepted,
            new(address.Scheme, address.IdnHost, address.Port,
                text.Contains('?', StringComparison.Ordinal) && (text.IndexOf('?', StringComparison.Ordinal)
                    < text.IndexOf('#', StringComparison.Ordinal) || !text.Contains('#', StringComparison.Ordinal)),
                text.Contains('#', StringComparison.Ordinal)));
    }
}
