using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Presentation;

namespace Kora.Application.Configuration;

public sealed record MarkdownDisplayCommand(MarkdownDisplayCommand.OperationType Operation, MarkdownDisplayMode? Value = null)
{
    public enum OperationType { Invalid, Inspect, Review, Reset, Confirm, Cancel }
    public const string OptionId = "responses.markdown-display";
    public const string Syntax = "get responses.markdown-display | status responses.markdown-display | "
        + "set responses.markdown-display to Rendered|Source | reset responses.markdown-display | "
        + "review markdown display change | confirm markdown display change | cancel markdown display change. "
        + "Set/reset prepare a review; confirmation is a separate save. Only future eligible final-answer Markdown Details.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
    [
        "get " + OptionId, "status " + OptionId, "set " + OptionId + " to Rendered",
        "set " + OptionId + " to Source", "reset " + OptionId,
        "review markdown display change", "confirm markdown display change", "cancel markdown display change",
    ];

    public static MarkdownDisplayCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (!FixedPhrases.Any(phrase => text.Equals(phrase, StringComparison.OrdinalIgnoreCase))
            && text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        var phrase = FixedPhrases.FirstOrDefault(value => text.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (phrase is null && !text.Contains(OptionId, StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("review markdown display", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("confirm markdown display", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("cancel markdown display", StringComparison.OrdinalIgnoreCase))
        { return null; }
        if (phrase is null || Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return new(MarkdownDisplayCommand.OperationType.Invalid); }
        if (phrase.StartsWith("get ", StringComparison.Ordinal) || phrase.StartsWith("status ", StringComparison.Ordinal))
        { return new(MarkdownDisplayCommand.OperationType.Inspect); }
        if (phrase.StartsWith("reset ", StringComparison.Ordinal)) { return new(MarkdownDisplayCommand.OperationType.Reset); }
        if (phrase.StartsWith("confirm ", StringComparison.Ordinal)) { return new(MarkdownDisplayCommand.OperationType.Confirm); }
        if (phrase.StartsWith("cancel ", StringComparison.Ordinal)) { return new(MarkdownDisplayCommand.OperationType.Cancel); }
        if (phrase.StartsWith("review ", StringComparison.Ordinal)) { return new(MarkdownDisplayCommand.OperationType.Review); }
        return new(MarkdownDisplayCommand.OperationType.Review,
            phrase.EndsWith("Source", StringComparison.Ordinal) ? MarkdownDisplayMode.Source : MarkdownDisplayMode.Rendered);
    }
}
