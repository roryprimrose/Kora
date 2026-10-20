using System.Text;

using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

/// <summary>Private literal preparation only; never rename or selection authority.</summary>
public sealed class SessionNamePreparationCommand
{
    private SessionNamePreparationCommand(Guid sessionId, SessionName? name, bool help)
    {
        SessionId = sessionId;
        Name = name;
        IsHelp = help;
    }

    public const string Syntax = "session name prepare <session-id> \"<literal-name>\" | session name help";
    public const string Boundary = "Preparation only in private native Sessions. Review the complete old and new names, "
        + "then use the separate native Rename gesture. No rename, activity renewal or automatic save occurs during preparation.";
    public Guid SessionId { get; }
    public SessionName? Name { get; }
    public bool IsHelp { get; }
    public bool IsValid => IsHelp || Name is not null;
    public override string ToString() => nameof(SessionNamePreparationCommand);

    public static SessionNamePreparationCommand? Parse(string input, string assistantName)
    {
        var text = input.TrimStart();
        var prefix = AssistantNameRules.Normalize(assistantName);
        if (!IsReserved(text) && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length
            && (text[prefix.Length] == ',' || char.IsWhiteSpace(text[prefix.Length])))
        {
            text = text[prefix.Length..].TrimStart(' ', ',', '\t');
        }
        if (!IsReserved(text))
        {
            return null;
        }
        static SessionNamePreparationCommand Invalid() => new(Guid.Empty, null, false);
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return Invalid();
        }
        if (text.Equals("session name help", StringComparison.OrdinalIgnoreCase))
        {
            return new(Guid.Empty, null, true);
        }
        const string prepare = "session name prepare ";
        if (!text.StartsWith(prepare, StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
        var targetAndName = text[prepare.Length..];
        var separator = targetAndName.IndexOf(' ', StringComparison.Ordinal);
        if (separator < 0) { return Invalid(); }
        var target = targetAndName[..separator];
        if (!SessionCommand.ExactId(target, out var id)
            || !string.Equals(target, id.ToString("D"), StringComparison.Ordinal))
        {
            return Invalid();
        }
        // Share the authoritative create/rename quote decoder and SessionName admission, not a second name grammar.
        var literal = SessionCommand.Parse("session create " + targetAndName[(separator + 1)..], AssistantNameRules.DefaultName);
        return literal!.Name is { } name
            ? new(id, name, false) : Invalid();
    }

    private static bool IsReserved(string text)
    {
        var head = text.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
        return head.Length >= 2 && head[0].Equals("session", StringComparison.OrdinalIgnoreCase)
            && head[1].Equals("name", StringComparison.OrdinalIgnoreCase);
    }
}
