using System.Text;
using Kora.Core.Configuration;
using Kora.Core.Network;

namespace Kora.Application.Network;

public sealed record WebPageCommand(Uri? Address, string? Error = null)
{
    public const string Syntax = "Use get web page <absolute HTTP or HTTPS address>.";
    public static IReadOnlyList<string> FixedPhrases { get; } = ["get web page"];

    public static WebPageCommand? Parse(string input, string assistantName)
    {
        ArgumentNullException.ThrowIfNull(input);
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        const string prefix = "get web page ";
        if (!text.StartsWith("get web page", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Encoding.UTF8.GetByteCount(input) > WebPageCapability.MaximumInputUtf8Bytes
            || input.Any(char.IsControl)
            || !text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(text[prefix.Length..], UriKind.Absolute, out var address))
        {
            return new(null, Syntax);
        }
        try
        {
            _ = WebPageAccessBinding.DestinationDigest(address);
            return new(address);
        }
        catch (ArgumentException)
        {
            return new(null, Syntax);
        }
    }
}
