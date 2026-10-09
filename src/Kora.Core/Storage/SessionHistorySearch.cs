using System.Security.Cryptography;
using System.Text;

using Kora.Core.Context;

namespace Kora.Core.Storage;

public sealed class SessionHistorySearch
{
    private readonly IReadOnlySet<string> terms;

    public SessionHistorySearch(string query)
    {
        if (!LocalFileRetrievalPolicy.TryQuery(query, out terms))
        {
            throw new ArgumentException("Use 1-256 characters / 512 UTF-8 bytes containing 1-32 distinct lexical terms of at most 64 characters.", nameof(query));
        }
        Digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('\n', terms.Order(StringComparer.Ordinal)))));
    }

    public string Digest { get; }

    public bool Matches(SessionHistoryEvent record, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (record.Availability is SessionHistoryAvailability.Gap or SessionHistoryAvailability.Redacted or SessionHistoryAvailability.Unavailable)
        {
            return false;
        }
        return Fields(record).Any(field => LocalFileRetrievalPolicy.Tokens(field, token).Any(terms.Contains));
    }

    private static IEnumerable<string> Fields(SessionHistoryEvent record)
    {
        yield return record.Kind.ToString();
        if (record.TaskState is { } task) { yield return task.ToString(); }
        if (record.Decision is { } decision) { yield return decision.ToString(); }
        if (record.QuestionStatus is { } status) { yield return status.ToString(); }
        if (record.Question is { } question) { yield return question; }
        foreach (var option in record.Options) { yield return option.Label; }
        if (record.Answer is { } answer) { yield return answer; }
        foreach (var choice in record.Choices) { yield return choice; }
    }
}
