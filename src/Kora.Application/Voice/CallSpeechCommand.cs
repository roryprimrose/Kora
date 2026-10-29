using System.Text;
using Kora.Core.Configuration;

namespace Kora.Application.Voice;

internal sealed record CallSpeechCommand(CallSpeechOperation? Operation)
{
    internal const string Syntax = "review call speech / confirm call speech / cancel call speech / call speech status";
    internal static IReadOnlyList<string> FixedPhrases { get; } =
        ["review call speech", "confirm call speech", "cancel call speech", "call speech status"];

    internal static CallSpeechCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim().TrimEnd('.', '?', '!').TrimEnd();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ','); }
        var words = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (!FixedPhrases.Any(phrase => words.StartsWith(phrase[..phrase.LastIndexOf(' ')], StringComparison.OrdinalIgnoreCase)))
        { return null; }
        var index = FixedPhrases.ToList().FindIndex(phrase => string.Equals(phrase, text, StringComparison.OrdinalIgnoreCase));
        return new(index >= 0 && Encoding.UTF8.GetByteCount(input) <= 1024 && !input.Any(char.IsControl)
            ? (CallSpeechOperation)index : null);
    }
}
