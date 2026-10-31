using System.Globalization;
using System.Text;

using Kora.Core.Hosting;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public const string RemainingWorkSyntax =
        "session remaining <session-id> | session remaining \"<exact readable name>\" | "
        + "what work remains in \"<exact readable name>\" | what do you have left to do in \"<exact readable name>\" | "
        + "session remaining choose <1-8> | session remaining cancel. "
        + "Names are exact and case-sensitive; double an embedded quote. Duplicate names require an exact bounded choice. "
        + "Voice requires the current assistant-name prefix. This answers passively, including Done conversations; setup status is separate.";
    public SessionName? RemainingTargetName { get; init; }
    public int RemainingChoice { get; init; }
    public bool IsRemainingWork => Operation is SessionCommandOperation.RemainingWork
        or SessionCommandOperation.RemainingWorkChoose or SessionCommandOperation.RemainingWorkCancel;

    private static SessionCommand? ParseRemainingWork(string text)
    {
        string[] prefixes = ["session remaining", "what work remains", "what do you have left to do in"];
        var prefix = prefixes.FirstOrDefault(value => text.Equals(value, StringComparison.OrdinalIgnoreCase)
            || text.StartsWith(value, StringComparison.OrdinalIgnoreCase) && text.Length > value.Length
                && char.IsWhiteSpace(text[value.Length]));
        if (prefix is null) { return null; }
        static SessionCommand Invalid() => new(SessionCommandOperation.Invalid, Error: RemainingWorkSyntax);
        if (Encoding.UTF8.GetByteCount(text) > MaximumInputBytes || text.Any(char.IsControl)) { return Invalid(); }
        var target = text[prefix.Length..].Trim();
        if (string.Equals(prefix, "what work remains", StringComparison.Ordinal))
        {
            if (!target.StartsWith("in ", StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
            target = target[3..].Trim();
        }
        if (string.Equals(prefix, "session remaining", StringComparison.Ordinal))
        {
            if (target.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            { return new(SessionCommandOperation.RemainingWorkCancel); }
            if (target.StartsWith("choose ", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(target[7..], NumberStyles.None, CultureInfo.InvariantCulture, out var choice) && choice is >= 1 and <= 8
                    ? new(SessionCommandOperation.RemainingWorkChoose) { RemainingChoice = choice } : Invalid();
            }
            if (ExactId(target, out var id)) { return new(SessionCommandOperation.RemainingWork, id); }
        }
        if (target.Length > 2 && target.StartsWith('"') && target.EndsWith('"'))
        {
            var name = target[1..^1];
            if (name.Replace("\"\"", string.Empty, StringComparison.Ordinal).Contains('"')) { return Invalid(); }
            try { return new(SessionCommandOperation.RemainingWork) { RemainingTargetName = new(name.Replace("\"\"", "\"", StringComparison.Ordinal)) }; }
            catch (InvalidDataException) { return Invalid(); }
        }
        return Invalid();
    }
}