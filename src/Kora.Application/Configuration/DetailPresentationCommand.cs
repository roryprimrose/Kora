using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Presentation;

namespace Kora.Application.Configuration;

public sealed record DetailPresentationCommand(DetailPresentationOperation Operation, DetailPresentationMode? Value = null)
{
    public const string OptionId = "responses.detail-presentation";
    public const string Syntax = "get responses.detail-presentation | status responses.detail-presentation | "
        + "set responses.detail-presentation to Offer|LinkOnly | reset responses.detail-presentation | "
        + "confirm detail presentation change | cancel detail presentation change | stop asking about details | offer detailed results. "
        + "Set/reset prepare a review, not a write. Automatic opening is unavailable.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
    [
        "get " + OptionId, "status " + OptionId, "set " + OptionId + " to Offer",
        "set " + OptionId + " to LinkOnly", "reset " + OptionId,
        "confirm detail presentation change", "cancel detail presentation change",
        "stop asking about details", "offer detailed results",
    ];

    public static DetailPresentationCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (!FixedPhrases.Any(phrase => text.Equals(phrase, StringComparison.OrdinalIgnoreCase))
            && text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        var phrase = FixedPhrases.FirstOrDefault(value => text.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (phrase is null && !text.Contains(OptionId, StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("confirm detail presentation", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("cancel detail presentation", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("stop asking about details", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("offer detailed results", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("always open detailed results", StringComparison.OrdinalIgnoreCase))
        { return null; }
        if (phrase is null || Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return new(DetailPresentationOperation.Invalid); }
        if (phrase.StartsWith("get ", StringComparison.Ordinal) || phrase.StartsWith("status ", StringComparison.Ordinal))
        { return new(DetailPresentationOperation.Inspect); }
        if (phrase.StartsWith("reset ", StringComparison.Ordinal)) { return new(DetailPresentationOperation.Reset); }
        if (phrase.StartsWith("confirm ", StringComparison.Ordinal)) { return new(DetailPresentationOperation.Confirm); }
        if (phrase.StartsWith("cancel ", StringComparison.Ordinal)) { return new(DetailPresentationOperation.Cancel); }
        return new(DetailPresentationOperation.Review,
            phrase.EndsWith("LinkOnly", StringComparison.Ordinal) || string.Equals(phrase, "stop asking about details", StringComparison.Ordinal)
                ? DetailPresentationMode.LinkOnly : DetailPresentationMode.Offer);
    }
}
