using System.Collections.Immutable;
using System.Text;

using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

/// <summary>Preparation only; IDs are selectors, never cancellation authority.</summary>
public sealed record SessionQueueCancellationReviewCommand(
    Guid SessionId, ImmutableArray<Guid> TaskIds, bool IsHelp = false, string? Error = null)
{
    public const string Syntax = "session queue cancellation help | session queue cancellation review <session-id> <task-id>[,<task-id>...]";
    public const string Boundary = "Prepare only the complete explicit pending subset in private native Sessions. "
        + "Separate one-shot native confirmation is required. No enqueue, cancellation commit, dispatch, pause/resume, "
        + "running-work effects, renewal or history removal.";

    public static SessionQueueCancellationReviewCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var prefix = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length
            && (text[prefix.Length] == ',' || char.IsWhiteSpace(text[prefix.Length])))
        { text = text[prefix.Length..].TrimStart(' ', ',', '\t'); }
        var head = text.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
        if (head.Length < 2 || !head[0].Equals("session", StringComparison.OrdinalIgnoreCase)
            || !head[1].Equals("queue", StringComparison.OrdinalIgnoreCase)) { return null; }
        static SessionQueueCancellationReviewCommand Invalid() => new(Guid.Empty, [], Error: Syntax + ". " + Boundary);
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return Invalid(); }
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4 || !words[2].Equals("cancellation", StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
        if (words.Length == 4 && words[3].Equals("help", StringComparison.OrdinalIgnoreCase))
        { return new(Guid.Empty, [], IsHelp: true); }
        if (words.Length != 6 || !words[3].Equals("review", StringComparison.OrdinalIgnoreCase)
            || !CanonicalId(words[4], out var session)) { return Invalid(); }
        var ids = words[5].Split(',');
        if (ids.Length > SessionQueueLimits.MaximumPendingPerSession) { return Invalid(); }
        var tasks = ImmutableArray.CreateBuilder<Guid>(ids.Length);
        foreach (var id in ids)
        {
            if (!CanonicalId(id, out var task) || tasks.Contains(task)) { return Invalid(); }
            tasks.Add(task);
        }
        return new(session, tasks.MoveToImmutable());
    }

    private static bool CanonicalId(string text, out Guid id) => SessionCommand.ExactId(text, out id)
        && string.Equals(text, id.ToString("D"), StringComparison.Ordinal);
}
