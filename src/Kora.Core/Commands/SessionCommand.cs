using System.Globalization;
using System.Text;

using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.Commands;

/// <summary>Exact identity grammar. Names are validated content, never selectors.</summary>
public sealed record SessionCommand(
    SessionCommandOperation Operation, Guid? SessionId = null, long Generation = 0,
    long MetadataRevision = 0, SessionName? Name = null, Guid? After = null,
    int Limit = SessionCommand.DefaultPageSize, string? Error = null)
{
    public const int DefaultPageSize = 25;
    public const int MaximumInputBytes = 1024;
    public const int MaximumResultBytes = 65536;
    public static IReadOnlyList<string> DiscoveryPhrases { get; } = ["session help", "session list", "task help"];
    public const string Syntax =
        "session help | session list [after <exact-id>] [limit <1-50>] | session status <exact-id> | "
        + "session inspect <exact-id> [tasks|questions] [after <exact-id>] [limit <1-50>] | "
        + "session create \"<name>\" | session rename <exact-id> <generation> <metadata-revision> \"<name>\" | "
        + "session done <exact-id> <generation> | session resume <exact-id> <generation>. "
        + "session history <exact-id> [after <generation>:<snapshot>:<sequence>] [limit <1-50>] | "
        + "session get <exact-id> <event-id>. History is passive; no composer, model context or replay. "
        + "IDs use canonical D GUIDs; revisions use decimal integers. Names use NFC single-line Unicode, "
        + "at most 120 scalars/480 UTF-8 bytes. Inside quotes, double a quote to include it. "
        + "No name, selected-window, delete, queue, executor or approval targeting.";

    public string PageKind { get; init; } = "tasks";
    public Guid? TaskId { get; init; }
    public Guid? HistoryEventId { get; init; }
    public SessionHistoryCursor? HistoryCursor { get; init; }
    public Guid? QuestionId { get; init; }
    public long TaskRevision { get; init; }
    public long QuestionRevision { get; init; }
    public const string TaskSyntax =
        "task status <session-id> <task-id> | task inspect <session-id> <task-id> | "
        + "task cancel <session-id> <task-id> <task-revision> <generation> <question-id> <question-revision>. "
        + "Only an admitted current-run local-version wait is cancellable before dispatch; no worker termination or replay.";

    public static SessionCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var prefix = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length
            && (text[prefix.Length] == ',' || char.IsWhiteSpace(text[prefix.Length])))
        {
            text = text[prefix.Length..].TrimStart(' ', ',', '\t');
        }
        var taskCommand = text.Equals("task", StringComparison.OrdinalIgnoreCase)
            || (text.StartsWith("task", StringComparison.OrdinalIgnoreCase) && text.Length > 4 && char.IsWhiteSpace(text[4]));
        if (!taskCommand && !text.Equals("session", StringComparison.OrdinalIgnoreCase)
            && !(text.StartsWith("session", StringComparison.OrdinalIgnoreCase)
                && char.IsWhiteSpace(text[7])))
        {
            return null;
        }
        SessionCommand Invalid(string reason) => new(SessionCommandOperation.Invalid, Error: reason + " " + Syntax);
        if (Encoding.UTF8.GetByteCount(input) > MaximumInputBytes
            || input.Any(char.IsControl))
        {
            return Invalid("Input exceeds 1024 UTF-8 bytes or contains control characters.");
        }
        if (taskCommand)
        {
            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 2 && tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
            {
                return new(SessionCommandOperation.Help);
            }
            if (tokens.Length >= 4 && ExactId(tokens[2], out var session) && ExactId(tokens[3], out var task))
            {
                if (tokens.Length == 4 && tokens[1].ToLowerInvariant() is "status" or "inspect")
                {
                    return new(tokens[1].Equals("status", StringComparison.OrdinalIgnoreCase)
                        ? SessionCommandOperation.TaskStatus : SessionCommandOperation.TaskInspect, session) { TaskId = task };
                }
                if (tokens.Length == 8 && tokens[1].Equals("cancel", StringComparison.OrdinalIgnoreCase)
                    && Revision(tokens[4], true, out var revision) && Revision(tokens[5], true, out var taskGeneration)
                    && ExactId(tokens[6], out var question) && Revision(tokens[7], true, out var questionRevision))
                {
                    return new(SessionCommandOperation.TaskCancel, session, taskGeneration)
                    {
                        TaskId = task, TaskRevision = revision, QuestionId = question, QuestionRevision = questionRevision,
                    };
                }
            }
            return Invalid(TaskSyntax);
        }
        var quote = text.IndexOf('"', StringComparison.Ordinal);
        SessionName? name = null;
        if (quote >= 0)
        {
            var quoted = text[quote..];
            var value = new StringBuilder();
            var closed = false;
            for (var i = 1; i < quoted.Length; i++)
            {
                if (quoted[i] != '"') { value.Append(quoted[i]); continue; }
                if (i + 1 < quoted.Length && quoted[i + 1] == '"') { value.Append('"'); i++; continue; }
                closed = i == quoted.Length - 1;
                break;
            }
            if (!closed || text[quote - 1] != ' ') { return Invalid("Use one final quoted name."); }
            try { name = new(value.ToString()); }
            catch (InvalidDataException exception) { return Invalid(exception.Message); }
            text = text[..quote].TrimEnd();
        }
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) { return Invalid("Specify a session operation."); }
        var operation = words[1].ToLowerInvariant();
        if (operation is "help" && words.Length == 2 && name is null) { return new(SessionCommandOperation.Help); }
        if (operation is "create" && words.Length == 2 && name is not null) { return new(SessionCommandOperation.Create, Name: name); }
        if (operation is "list" && name is null) { return Page(new(SessionCommandOperation.List), words, 2); }
        if (words.Length < 3 || !ExactId(words[2], out var id)) { return Invalid("An exact nonempty session ID is required."); }
        if (operation is "status" && words.Length == 3 && name is null) { return new(SessionCommandOperation.Status, id); }
        if (operation is "get" && words.Length == 4 && name is null && ExactId(words[3], out var eventId))
        {
            return new(SessionCommandOperation.HistoryGet, id) { HistoryEventId = eventId };
        }
        if (operation is "history" && name is null)
        {
            var start = 3;
            SessionHistoryCursor? cursor = null;
            if (words.Length > start && words[start].Equals("after", StringComparison.OrdinalIgnoreCase))
            {
                if (words.Length <= start + 1) { return Invalid("Specify generation:snapshot:sequence."); }
                var segments = words[start + 1].Split(':');
                if (segments.Length != 3 || !Revision(segments[0], true, out var revision)
                    || !Revision(segments[1], true, out var snapshot) || !Revision(segments[2], true, out var sequence)
                    || sequence > snapshot)
                {
                    return Invalid("Use the exact returned generation:snapshot:sequence cursor.");
                }
                cursor = new(new(id), new(revision), snapshot, sequence);
                start += 2;
            }
            var page = Page(new(SessionCommandOperation.History, id) { HistoryCursor = cursor }, words, start);
            return page.After is null ? page : Invalid("History requires a generation:snapshot:sequence cursor.");
        }
        if (operation is "inspect" && name is null)
        {
            var start = 3;
            var kind = "tasks";
            if (words.Length > start && words[start].ToLowerInvariant() is "tasks" or "questions")
            {
                kind = words[start++].ToLowerInvariant();
            }
            return Page(new(SessionCommandOperation.Inspect, id) { PageKind = kind }, words, start);
        }
        if (words.Length >= 4 && Revision(words[3], positive: true, out var generation))
        {
            if (operation is "done" or "resume" && words.Length == 4 && name is null)
            {
                return new(operation is "done" ? SessionCommandOperation.Done : SessionCommandOperation.Resume, id, generation);
            }
            if (operation is "rename" && words.Length == 5 && name is not null
                && Revision(words[4], positive: false, out var metadata))
            {
                return new(SessionCommandOperation.Rename, id, generation, metadata, name);
            }
        }
        return Invalid("Unsupported or malformed session operation.");

        SessionCommand Page(SessionCommand command, string[] tokens, int start)
        {
            Guid? after = null;
            var limit = DefaultPageSize;
            if (tokens.Length > start && tokens[start].Equals("after", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Length <= start + 1 || !ExactId(tokens[start + 1], out var cursor)) { return Invalid("Invalid exact page cursor."); }
                after = cursor;
                start += 2;
            }
            if (tokens.Length > start && tokens[start].Equals("limit", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Length <= start + 1 || !Revision(tokens[start + 1], true, out var count)
                    || count > SessionPage<SessionWorkspaceEntry>.MaximumRecords) { return Invalid("Page size must be 1-50."); }
                limit = (int)count;
                start += 2;
            }
            return tokens.Length == start ? command with { After = after, Limit = limit } : Invalid("Invalid page arguments.");
        }
    }

    private static bool ExactId(string text, out Guid id) =>
        Guid.TryParseExact(text, "D", out id) && id != Guid.Empty;

    private static bool Revision(string text, bool positive, out long value) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= (positive ? 1 : 0);
}
