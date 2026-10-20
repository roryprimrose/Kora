using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record ResponseModeCommand(AppearanceCommandOperation Operation, ResponseOutputMode? Value = null, string? Error = null)
{
    private const string VisualPhrase = "use visual responses";
    private const string SpokenPhrase = "use spoken responses";
    private const string HybridPhrase = "use spoken and visual responses";
    private const string StatusPhrase = "check response mode";
    private static readonly IReadOnlyDictionary<string, ResponseModeCommand> NaturalCommands =
        new Dictionary<string, ResponseModeCommand>(StringComparer.OrdinalIgnoreCase)
        {
            [VisualPhrase] = new(AppearanceCommandOperation.Set, ResponseOutputMode.VisualOnly),
            [SpokenPhrase] = new(AppearanceCommandOperation.Set, ResponseOutputMode.VoiceOnly),
            [HybridPhrase] = new(AppearanceCommandOperation.Set, ResponseOutputMode.Hybrid),
            [StatusPhrase] = new(AppearanceCommandOperation.Get),
        };

    public const string OptionId = "responses.default-mode";
    public const string Syntax = "list response settings | get responses.default-mode | status responses.default-mode | "
        + "set responses.default-mode to Hybrid|VoiceOnly|VisualOnly | reset responses.default-mode | "
        + VisualPhrase + " | " + SpokenPhrase + " | " + HybridPhrase + " | " + StatusPhrase;
    public static IReadOnlyList<string> FixedPhrases { get; } =
        new[] { "list response settings", "get " + OptionId, "status " + OptionId, "reset " + OptionId,
            "set " + OptionId + " to Hybrid", "set " + OptionId + " to VoiceOnly", "set " + OptionId + " to VisualOnly" }
        .Concat(NaturalCommands.Keys).ToArray();

    public static ResponseModeCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var original = text;
        var name = AssistantNameRules.Normalize(assistantName);
        // An alias may itself begin with, or equal, the current assistant name.
        if (!NaturalCommands.ContainsKey(text) && text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t').TrimStart();
        }
        if (!IsReservedNatural(original) && !IsReservedNatural(text)
            && !text.StartsWith("list response", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get responses.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status responses.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set responses.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset responses.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Response input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
        if (NaturalCommands.TryGetValue(text, out var natural)) { return natural; }
        if (text.Equals("list response settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        var prefix = "set " + OptionId + " to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var value = text[prefix.Length..];
            foreach (var mode in Enum.GetValues<ResponseOutputMode>())
            {
                if (value.Equals(mode.ToString(), StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Set, mode); }
            }
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }

    private static bool IsReservedNatural(string text)
    {
        if (AfterWord(text, "use") is { } target)
        {
            return target.StartsWith("visual", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("spoken", StringComparison.OrdinalIgnoreCase);
        }
        return AfterWord(text, "check") is { } status
            && AfterWord(status, "response") is { } mode
            && mode.StartsWith("mode", StringComparison.OrdinalIgnoreCase);
    }

    private static string? AfterWord(string text, string word) =>
        text.StartsWith(word, StringComparison.OrdinalIgnoreCase) && text.Length > word.Length && char.IsWhiteSpace(text[word.Length])
            ? text[word.Length..].TrimStart() : null;
}
