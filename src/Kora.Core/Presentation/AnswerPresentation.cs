namespace Kora.Core.Presentation;

/// <summary>Passive presentation only; the complete original answer remains the source of record.</summary>
public sealed record AnswerPresentation(string Brief, bool HasDetails)
{
    public const string ModelPolicy =
        "For a complex answer, begin with 'Brief response:' on its own line, followed by a concise "
        + "plain-text summary of at most 80 words, then a blank line and 'Detailed response:' on its own line "
        + "followed by the complete answer in Markdown, including diagrams/code/tables as needed. "
        + "Keep all necessary warnings in the brief. Do not ask whether to open details yourself: "
        + "Kora owns that question and all window controls. For a short answer, return the answer normally.";
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
