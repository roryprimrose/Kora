namespace Kora.Core.Presentation;

/// <summary>Passive presentation only; the complete original answer remains the source of record.</summary>
public sealed record AnswerPresentation(string Brief, bool HasDetails)
{
    public const string ModelPolicy =
        "Complex answer: 'Brief response:' on its own line, plain-text summary of at most 80 words, "
        + "blank line, 'Detailed response:' on its own line, complete Markdown answer with diagrams/code/tables as needed. "
        + "Keep all necessary warnings in the brief. Kora owns the detail question and all window controls; "
        + "do not ask whether to open details. Short answer: answer normally.";
    public const string Offer = "Would you like to view the detailed response?";
    public const string Available = "A detailed response is available.";

    public static AnswerPresentation From(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string start = "Brief response:\n";
        const string separator = "\n\nDetailed response:\n";
        if (normalized.StartsWith(start, StringComparison.Ordinal))
        {
            var split = normalized.IndexOf(separator, start.Length, StringComparison.Ordinal);
            if (split >= 0)
            {
                var brief = normalized[start.Length..split].Trim();
                var details = normalized[(split + separator.Length)..];
                if (brief.Length is > 0 and <= 1024 && Words(brief) <= 80
                    && !string.IsNullOrWhiteSpace(details))
                { return new(brief, true); }
            }
        }
        var complex = Words(body) > 120 || normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Length > 3
            || normalized.Contains("```", StringComparison.Ordinal) || normalized.Contains('|', StringComparison.Ordinal);
        return new(complex ? Available : body, complex);
    }

    private static int Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
