using Kora.Core.Configuration;

namespace Kora.Application.Network;

/// <summary>Exact original-user phrases; page text and URI arguments are never commands.</summary>
public sealed record WebSummaryCommand(Guid? Confirmation = null, bool Cancel = false, string? Error = null)
{
    public const string RequestPhrase = "summarize current web result";
    public const string ConfirmPrefix = "confirm local web summary ";
    public const string Syntax = "Use summarize current web result, confirm local web summary <exact review ID>, or cancel local web summary.";
    public static IReadOnlyList<string> FixedPhrases { get; } = [RequestPhrase, "cancel local web summary"];

    public static WebSummaryCommand? Parse(string input, string assistantName)
    {
        ArgumentNullException.ThrowIfNull(input);
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',');
        }
        var recognized = text.StartsWith(RequestPhrase, StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("confirm local web summary", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("cancel local web summary", StringComparison.OrdinalIgnoreCase);
        if (input.Any(char.IsControl)) { return recognized ? new(Error: Syntax) : null; }
        if (string.Equals(text, RequestPhrase, StringComparison.OrdinalIgnoreCase)) { return new(); }
        if (string.Equals(text, "cancel local web summary", StringComparison.OrdinalIgnoreCase)) { return new(Cancel: true); }
        if (text.StartsWith(ConfirmPrefix, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(text[ConfirmPrefix.Length..], "D", out var id) && id != Guid.Empty)
        { return new(id); }
        return recognized ? new(Error: Syntax) : null;
    }
}
