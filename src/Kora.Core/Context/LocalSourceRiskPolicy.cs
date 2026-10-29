using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kora.Core.Conversation;

namespace Kora.Core.Context;

/// <summary>Versioned, reject-only assistance for explicitly consented private LocalOnly use, never non-sensitivity proof.</summary>
public static class LocalSourceRiskPolicy
{
    public const long Revision = 1;
    public const string Version = "private-local-source-use-v1";
    public const string Disclosure = "Private local-only source use. Best-effort credential checks can miss arbitrary secrets; "
        + "ordinary sensitive content may be processed locally. Detected risks cannot be overridden. "
        + "No secret-free guarantee, hosted fallback or additional permission is granted.";
    public const int MaximumParts = 16;
    public const int MaximumPartUtf8Bytes = LocalFilePolicy.MaximumBytes;
    public const int MaximumTotalUtf8Bytes = 2 * MaximumPartUtf8Bytes;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Regex Risk = new(
        """(?:^|[\s{,;?&])["']?(?:password|passwd|pwd|secret|client[_ -]?secret|api[_ -]?key|access[_ -]?token|refresh[_ -]?token|session[_ -]?token|private[_ -]?key|connection[_ -]?string|account[_ -]?key|shared[_ -]?access[_ -]?signature|sig|authorization)["']?\s*[:=]|-----BEGIN (?:[A-Z0-9 ]*PRIVATE KEY|PGP PRIVATE KEY BLOCK)-----|\b[a-z][a-z0-9+.-]*://[^\s/@:]+:[^\s/@]+@|(?:^|[^a-z0-9_])(?:sk-(?:proj-)?[a-z0-9_-]{20,}|(?:AKIA|ASIA)[A-Z0-9]{16})(?:$|[^a-z0-9_])""",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));

    public static LocalSourceRiskOutcome Evaluate(IReadOnlyList<string> parts, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(parts);
        token.ThrowIfCancellationRequested();
        if (parts.Count is < 1 or > MaximumParts) { return LocalSourceRiskOutcome.Unavailable; }
        var total = 0;
        foreach (var part in parts)
        {
            token.ThrowIfCancellationRequested();
            if (part is null || part.Length > MaximumPartUtf8Bytes) { return LocalSourceRiskOutcome.Unavailable; }
            int bytes;
            try { bytes = Utf8.GetByteCount(part); }
            catch (EncoderFallbackException) { return LocalSourceRiskOutcome.Unavailable; }
            total += bytes;
            if (bytes > MaximumPartUtf8Bytes || total > MaximumTotalUtf8Bytes) { return LocalSourceRiskOutcome.Unavailable; }
            if (!AcceptedText(part)) { return LocalSourceRiskOutcome.DetectedRisk; }
            var start = part.AsSpan().TrimStart();
            if (!start.IsEmpty && start[0] is '{' or '[' or '"')
            {
                try
                {
                    using var document = JsonDocument.Parse(part, new JsonDocumentOptions { MaxDepth = 32 });
                    if (!AcceptedJson(document.RootElement, token)) { return LocalSourceRiskOutcome.DetectedRisk; }
                }
                catch (JsonException) { return LocalSourceRiskOutcome.Unavailable; }
            }
        }
        token.ThrowIfCancellationRequested();
        return LocalSourceRiskOutcome.AdmittedPrivateLocalOnly;
    }

    private static bool AcceptedText(string text)
    {
        if (Risk.IsMatch(text)) { return false; }
        try { ConversationText.RequireNoCredentials(text); }
        catch (ConversationCredentialException) { return false; }
        return true;
    }

    private static bool AcceptedJson(JsonElement value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    if (!AcceptedText(property.Name + "=") || !AcceptedJson(property.Value, token)) { return false; }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (!AcceptedJson(item, token)) { return false; }
                }
                break;
            case JsonValueKind.String:
                return AcceptedText(value.GetString()!);
        }
        return true;
    }
}
