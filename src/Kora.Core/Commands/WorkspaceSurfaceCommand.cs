using Kora.Core.Artifacts;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

public sealed record WorkspaceSurfaceCommand(WorkspaceSurfaceTarget Target, int? SessionPosition = null,
    SessionName? ConversationName = null, string? ObservationName = null)
{
    /// <summary>Position zero denotes the last row of the presented page; positive positions count from its top.</summary>
    public const int LastSessionPosition = 0;

    public static bool MatchesObservationName(string input, string presented) =>
        string.Equals(BuiltInCommandRouter.Normalize(input), BuiltInCommandRouter.Normalize(presented), StringComparison.Ordinal);

    private static readonly string[] Ordinals =
    [
        "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth",
        "eleventh", "twelfth", "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth",
        "eighteenth", "nineteenth", "twentieth", "twenty-first", "twenty-second", "twenty-third",
        "twenty-fourth", "twenty-fifth",
    ];
    private static readonly (string Phrase, WorkspaceSurfaceTarget Target)[] Routes =
    [
        ("open all work", WorkspaceSurfaceTarget.AllWork),
        ("show sessions needing an answer", WorkspaceSurfaceTarget.WaitingQuestions),
        ("open compact conversation", WorkspaceSurfaceTarget.CompactConversation),
        ("open sessions", WorkspaceSurfaceTarget.Sessions),
        ("show sessions", WorkspaceSurfaceTarget.Sessions),
        ("show me the current sessions", WorkspaceSurfaceTarget.Sessions),
        ("expand conversation", WorkspaceSurfaceTarget.Sessions),
        ("collapse conversation", WorkspaceSurfaceTarget.CompactConversation),
        ("show me the detailed results", WorkspaceSurfaceTarget.Details),
        ("show detailed results", WorkspaceSurfaceTarget.Details),
        ("show details", WorkspaceSurfaceTarget.Details),
        ("hide details", WorkspaceSurfaceTarget.HideDetails),
        ("show session logs", WorkspaceSurfaceTarget.SessionLogs),
        ("show session audit", WorkspaceSurfaceTarget.SessionAudit),
    ];

    public static IReadOnlyList<string> FixedPhrases { get; } =
        Array.AsReadOnly(Routes.Select(route => route.Phrase)
            .Concat(Ordinals.Select(ordinal => "select the " + ordinal + " session"))
            .Concat(["select the top session", "select the last session"]).ToArray());

    public static WorkspaceSurfaceCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = input.Trim();
        if (!FixedPhrases.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            text = ArtifactCommandRouter.RemoveActivationPrefix(text, assistantName);
        }
        if (text.StartsWith("open conversation", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("read conversation", StringComparison.OrdinalIgnoreCase))
        {
            if (input.Any(char.IsControl) || text.Length > 512
                || text.Length < 20 || text[17] != ' ' || text[18] != '"' || text[^1] != '"')
            { return new(WorkspaceSurfaceTarget.Invalid); }
            var literal = text[19..^1];
            if (literal.Contains('"', StringComparison.Ordinal))
            { return new(WorkspaceSurfaceTarget.Invalid); }
            try { return new(WorkspaceSurfaceTarget.OpenNamedConversation, ConversationName: new(literal)); }
            catch (InvalidDataException) { return new(WorkspaceSurfaceTarget.Invalid); }
        }
        if (text.StartsWith("open observed result", StringComparison.OrdinalIgnoreCase))
        {
            const string prefix = "open observed result ";
            return !input.Any(char.IsControl) && text.Length <= 256
                && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? new(WorkspaceSurfaceTarget.ObservedResult, ObservationName: text[prefix.Length..])
                : new(WorkspaceSurfaceTarget.Invalid);
        }
        var normalized = BuiltInCommandRouter.Normalize(text);
        if (!normalized.StartsWith("open all work", StringComparison.Ordinal)
            && !normalized.StartsWith("open saved conversation", StringComparison.Ordinal)
            && !normalized.StartsWith("open compact", StringComparison.Ordinal)
            && !normalized.StartsWith("open sessions", StringComparison.Ordinal)
            && !normalized.StartsWith("show sessions", StringComparison.Ordinal)
            && !normalized.StartsWith("show me the current sessions", StringComparison.Ordinal)
            && !normalized.StartsWith("expand conversation", StringComparison.Ordinal)
            && !normalized.StartsWith("collapse conversation", StringComparison.Ordinal)
            && !normalized.StartsWith("show me the detailed results", StringComparison.Ordinal)
            && !normalized.StartsWith("show detailed results", StringComparison.Ordinal)
            && !normalized.StartsWith("show details", StringComparison.Ordinal)
            && !normalized.StartsWith("hide details", StringComparison.Ordinal)
            && !normalized.StartsWith("show session logs", StringComparison.Ordinal)
            && !normalized.StartsWith("show session audit", StringComparison.Ordinal)
            && !(normalized.StartsWith("select the ", StringComparison.Ordinal)
                && normalized.Contains(" session", StringComparison.Ordinal)))
        {
            return null;
        }
        foreach (var route in Routes)
        {
            if (string.Equals(text, route.Phrase, StringComparison.OrdinalIgnoreCase)
                && !input.Any(char.IsControl))
            {
                return new(route.Target);
            }
        }
        if (!input.Any(char.IsControl))
        {
            if (string.Equals(text, "select the top session", StringComparison.OrdinalIgnoreCase))
            { return new(WorkspaceSurfaceTarget.SelectSession, 1); }
            if (string.Equals(text, "select the last session", StringComparison.OrdinalIgnoreCase))
            { return new(WorkspaceSurfaceTarget.SelectSession, LastSessionPosition); }
            for (var index = 0; index < Ordinals.Length; index++)
            {
                if (string.Equals(text, "select the " + Ordinals[index] + " session", StringComparison.OrdinalIgnoreCase))
                { return new(WorkspaceSurfaceTarget.SelectSession, index + 1); }
            }
        }
        return new(WorkspaceSurfaceTarget.Invalid);
    }
}
