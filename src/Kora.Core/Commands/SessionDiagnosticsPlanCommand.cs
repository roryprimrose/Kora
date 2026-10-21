using System.Collections.Immutable;
using System.Text;

using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

/// <summary>Exact preparation-only grammar for the existing fixed local diagnostics review.</summary>
public sealed record SessionDiagnosticsPlanCommand(Guid SessionId,
    ImmutableArray<SessionQueueReadProfile> Profiles, bool IsHelp = false, string? Error = null)
{
    public const string Syntax = "session diagnostics plan <session-id> <profiles> | session diagnostics plan help";
    public const string Boundary = "Prepare only in private native Sessions; no enqueue or dispatch. "
        + "Use a nonempty lowercase canonical D session GUID and an explicit nonempty comma-separated subset of "
        + "version,capabilities,readiness,runtimes in that order, without duplicates, spaces or defaults. "
        + "Enqueue requires a separate native confirmation; typed/voice confirmation is unavailable.";

    public static SessionDiagnosticsPlanCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var prefix = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length
            && (text[prefix.Length] == ',' || char.IsWhiteSpace(text[prefix.Length])))
        {
            text = text[prefix.Length..].TrimStart(' ', ',', '\t');
        }
        var head = text.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
        if (head.Length < 2 || !head[0].Equals("session", StringComparison.OrdinalIgnoreCase)
            || !head[1].Equals("diagnostics", StringComparison.OrdinalIgnoreCase))
        { return null; }

        static SessionDiagnosticsPlanCommand Invalid() => new(Guid.Empty, [], Error: Syntax + ". " + Boundary);
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return Invalid(); }
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 4 && words[2].Equals("plan", StringComparison.OrdinalIgnoreCase)
            && words[3].Equals("help", StringComparison.OrdinalIgnoreCase))
        { return new(Guid.Empty, [], IsHelp: true); }
        if (words.Length != 5 || !words[2].Equals("plan", StringComparison.OrdinalIgnoreCase)
            || !SessionCommand.ExactId(words[3], out var id)
            || !string.Equals(words[3], id.ToString("D"), StringComparison.Ordinal))
        { return Invalid(); }
        var profiles = ImmutableArray.CreateBuilder<SessionQueueReadProfile>();
        var previous = -1;
        foreach (var item in words[4].Split(','))
        {
            var index = item switch
            {
                "version" => 0,
                "capabilities" => 1,
                "readiness" => 2,
                "runtimes" => 3,
                _ => -1,
            };
            if (index <= previous) { return Invalid(); }
            previous = index;
            profiles.Add(index switch
            {
                0 => SessionQueueReadProfile.Version,
                1 => SessionQueueReadProfile.Capabilities,
                2 => SessionQueueReadProfile.Readiness,
                _ => SessionQueueReadProfile.Runtimes,
            });
        }
        return new(id, profiles.ToImmutable());
    }
}
