using System.Text;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record PreapprovedUriCommand(
    PreapprovedUriCommandOperation Operation,
    string? Pattern = null,
    string? Error = null)
{
    public const string Syntax = "Use list preapproved addresses, add preapproved address <HTTP-or-HTTPS-pattern>, "
        + "remove preapproved address <pattern>, or clear preapproved addresses.";

    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list preapproved addresses", "add preapproved address", "remove preapproved address", "clear preapproved addresses"];

    public static PreapprovedUriCommand? Parse(string input, string assistantName)
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

        if (!text.StartsWith("list preapproved address", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("add preapproved address", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("remove preapproved address", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("clear preapproved address", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Encoding.UTF8.GetByteCount(input) > 4096 || input.Any(char.IsControl))
        {
            return new(PreapprovedUriCommandOperation.Clarify, Error: Syntax);
        }
        if (text.Equals("list preapproved addresses", StringComparison.OrdinalIgnoreCase))
        {
            return new(PreapprovedUriCommandOperation.List);
        }
        if (text.Equals("clear preapproved addresses", StringComparison.OrdinalIgnoreCase))
        {
            return new(PreapprovedUriCommandOperation.Clear);
        }
        foreach (var candidate in new[]
        {
            (Prefix: "add preapproved address ", Operation: PreapprovedUriCommandOperation.Add),
            (Prefix: "remove preapproved address ", Operation: PreapprovedUriCommandOperation.Remove),
        })
        {
            if (text.StartsWith(candidate.Prefix, StringComparison.OrdinalIgnoreCase)
                && text.Length > candidate.Prefix.Length)
            {
                return new(candidate.Operation, text[candidate.Prefix.Length..]);
            }
        }
        return new(PreapprovedUriCommandOperation.Clarify, Error: Syntax);
    }
}
