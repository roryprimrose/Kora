using System.Text;
using System.Text.RegularExpressions;

namespace Kora.Core.Conversation;

/// <summary>Bounded local structural checks; no inference, entropy scoring, expansion or credential verification.</summary>
internal static class ConversationCredentialScreening
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking;
    private static readonly Regex Assignments = new(
        """(?:^|[\s{,;])["']?(?:password|client_secret|api_key)["']?\s*[:=]\s*(?:"(?<value>[^"\r\n]*)"|'(?<value>[^'\r\n]*)'|(?<value>\$\{[^}\r\n]*\})|(?<value>[^\s,;}\r\n]+))""",
        Options, TimeSpan.FromSeconds(1));
    private static readonly Regex Authorization = new(
        """(?:^|[\s{,;])["']?Authorization["']?\s*[:=]\s*["']?(?<scheme>Bearer|Basic)\s+(?<value>\$\{[^}\r\n]*\}|[^\s"',;}\r\n]+)""",
        Options, TimeSpan.FromSeconds(1));
    private static readonly Regex Tokens = new(
        """(?:^|[^a-zA-Z0-9_])(?<value>gh[pousr]_[a-zA-Z0-9]{36}|github_pat_[a-zA-Z0-9]{22}_[a-zA-Z0-9]{59}|eyJ[a-zA-Z0-9_-]{17,}\.eyJ[a-zA-Z0-9_-]{17,}\.[a-zA-Z0-9_-]{32,})(?:$|[^a-zA-Z0-9_])""",
        Options, TimeSpan.FromSeconds(1));
    private static readonly Regex PemStarts = new(
        """-----BEGIN (?<label>(?:RSA |EC |DSA |OPENSSH )?PRIVATE KEY)-----""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
    private static readonly string[] PlaceholderValues = ["example", "sample", "placeholder", "redacted", "changeme", "password", "token", "user", "username", "secret", "api_key", "client_secret"];
    private static readonly string[] PlaceholderPrefixes = ["example-", "example_", "sample-", "dummy-", "fake-", "your_", "your-", "not-a-real-"];

    internal static void RequireAccepted(string text)
    {
        foreach (Match opening in PemStarts.Matches(text))
        {
            var label = opening.Groups["label"].Value;
            var bodyStart = opening.Index + opening.Length;
            var end = text.IndexOf("-----END " + label + "-----", bodyStart, StringComparison.Ordinal);
            var candidate = text[bodyStart..(end < 0 ? text.Length : end)].Trim();
            if (Placeholder(candidate)) { continue; }
            // A malformed structural candidate is also refused, never repaired or classified as ordinary prose.
            if (end >= 0 || candidate.Length >= 64) { throw new ConversationCredentialException(); }
        }
        foreach (Match match in Assignments.Matches(text))
        {
            var value = match.Groups["value"].Value;
            if (value.Length >= 8 && !Placeholder(value)) { throw new ConversationCredentialException(); }
        }
        Span<byte> decoded = stackalloc byte[ConversationText.MaximumUtf16Characters];
        foreach (Match match in Authorization.Matches(text))
        {
            var value = match.Groups["value"].Value;
            if (Placeholder(value)) { continue; }
            if (string.Equals(match.Groups["scheme"].Value, "Basic", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length < 12) { continue; }
                if (!Convert.TryFromBase64String(value, decoded, out var count))
                { throw new ConversationCredentialException(); }
                var credentials = Encoding.UTF8.GetString(decoded[..count]);
                decoded.Clear();
                var colon = credentials.IndexOf(':');
                if (colon > 0 && colon < credentials.Length - 1
                    && Placeholder(credentials[(colon + 1)..]))
                { continue; }
                throw new ConversationCredentialException();
            }
            if (value.Length >= 24) { throw new ConversationCredentialException(); }
        }
        foreach (Match match in Tokens.Matches(text))
        {
            var value = match.Groups["value"].Value;
            var tail = value[(value.IndexOf('_') + 1)..];
            if (!Placeholder(value) && !tail.All(character => character is 'x' or 'X' or '*'))
            { throw new ConversationCredentialException(); }
        }
    }

    private static bool Placeholder(string value)
    {
        if (value.Length == 0 || string.Equals(value, "...", StringComparison.Ordinal)
            || value.All(character => character == 'x' || character == 'X' || character == '*')) { return true; }
        if (value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}')) { return true; }
        var plain = value.Trim('<', '>');
        if (PlaceholderValues.Contains(plain, StringComparer.OrdinalIgnoreCase)) { return true; }
        return PlaceholderPrefixes.Any(prefix => plain.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
