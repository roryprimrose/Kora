using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record QuietHoursCommand(AppearanceCommandOperation Operation, QuietHoursSchedule? Value = null,
    bool? ReviewDecision = null, string? Error = null)
{
    public const string OptionId = "notices.quiet-hours";
    public const string Syntax = "get notices.quiet-hours | set notices.quiet-hours to Off | "
        + "set notices.quiet-hours to weekly Mon,Tue,Wed,Thu,Fri 09:00 17:00 UTC | reset notices.quiet-hours | "
        + "confirm notices.quiet-hours | cancel notices.quiet-hours. Use an explicit installed zone ID, ordered day set and 24-hour times.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["get notices.quiet-hours", "set notices.quiet-hours to Off", "reset notices.quiet-hours",
            "confirm notices.quiet-hours", "cancel notices.quiet-hours"];

    public static QuietHoursCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        var verbs = new[] { "get", "set", "reset", "confirm", "cancel" };
        if (!verbs.Any(verb => text.StartsWith(verb + " " + OptionId, StringComparison.OrdinalIgnoreCase)
            && (text.Length == verb.Length + 1 + OptionId.Length || char.IsWhiteSpace(text[verb.Length + 1 + OptionId.Length]))))
        { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return new(AppearanceCommandOperation.Clarify, Error: Syntax); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        if (text.Equals("confirm " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Set, ReviewDecision: true); }
        if (text.Equals("cancel " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Set, ReviewDecision: false); }
        const string prefix = "set " + OptionId + " to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            try { return new(AppearanceCommandOperation.Set, QuietHoursSchedule.Parse(text[prefix.Length..])); }
            catch (ArgumentException) { return new(AppearanceCommandOperation.Clarify, Error: Syntax); }
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
