using System.Collections.Immutable;

using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    public Task<ConversationSearchPage> SearchConversationAsync(HostId<SessionIdentity> session, string query,
        ConversationSearchCursor? cursor, int limit, CancellationToken token) =>
        ReadAsync(async () =>
        {
            session.Validate();
            if (limit is <= 0 or > ConversationText.MaximumPageRecords) { throw new ArgumentOutOfRangeException(nameof(limit)); }
            var search = new ConversationSearch(query);
            if (cursor is not null && !string.Equals(cursor.QueryDigest, search.Digest, StringComparison.Ordinal))
            { throw new ArgumentException("CursorMismatch: start a fresh conversation search.", nameof(cursor)); }
            var continuation = cursor?.Conversation;
            var records = ImmutableArray.CreateBuilder<ConversationMessage>();
            var scanned = 0;
            ConversationSearchPage result;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var page = await ReadConversationPageAsync(session, continuation,
                    ConversationText.MaximumPageRecords, token).ConfigureAwait(false);
                if (page.Session.SessionId != session || page.Ceiling is < 0 or > ConversationText.MaximumSessionMessages
                    || continuation is not null && (page.Session.Generation != continuation.Generation
                        || page.Ceiling != continuation.Ceiling || page.HeadRevision != continuation.HeadRevision))
                { throw new InvalidDataException("Conversation search source does not match its exact snapshot."); }
                var after = continuation?.After ?? 0;
                if (page.Messages.IsEmpty && after < page.Ceiling)
                { throw new InvalidDataException("The exact conversation snapshot has an unavailable sequence gap."); }
                ConversationSearchCursor? Next(long sequence) => sequence < page.Ceiling
                    ? new(new(session, page.Session.Generation, page.Ceiling, sequence, page.HeadRevision), search.Digest) : null;
                result = new(page.Session, page.HeadRevision, page.Ceiling, search.Digest, records.ToImmutable(), scanned, Next(after));
                foreach (var message in page.Messages)
                {
                    token.ThrowIfCancellationRequested();
                    message.Validate();
                    if (message.Request.SessionId != session || message.Generation.Value > page.Session.Generation.Value
                        || message.Sequence != after + 1 || message.Sequence > page.Ceiling)
                    { throw new InvalidDataException("Conversation search requires exact ordered immutable messages."); }
                    if (search.Matches(message, token))
                    {
                        var candidate = result with { Messages = records.ToImmutable().Add(message),
                            Scanned = scanned + 1, Next = Next(message.Sequence) };
                        // Later nonmatches can grow the counter and cursor even without adding another body.
                        var budget = candidate with { Scanned = ConversationText.MaximumSessionMessages,
                            Next = Next(page.Ceiling - 1) };
                        if (ConversationSearchPage.Serialize(budget).Length > ConversationText.MaximumPageUtf8Bytes)
                        {
                            break;
                        }
                        records.Add(message);
                    }
                    scanned++;
                    after = message.Sequence;
                    result = result with { Messages = records.ToImmutable(), Scanned = scanned, Next = Next(after) };
                    if (records.Count == limit || scanned == ConversationText.MaximumSessionMessages) { break; }
                }
                if (result.Next is null || records.Count == limit || scanned == ConversationText.MaximumSessionMessages
                    || after != page.Messages.LastOrDefault()?.Sequence) { break; }
                continuation = result.Next.Conversation;
            }
            // The provider does not accept After == Ceiling. Re-read the last original row to validate even an exhausted snapshot.
            var fence = result.Ceiling == 0 ? null
                : new ConversationCursor(session, result.Session.Generation, result.Ceiling, result.Ceiling - 1, result.HeadRevision);
            var current = await ReadConversationPageAsync(session, fence, 1, token).ConfigureAwait(false);
            if (current.Session != result.Session || current.Ceiling != result.Ceiling || current.HeadRevision != result.HeadRevision)
            { throw new ConversationSnapshotChangedException(); }
            token.ThrowIfCancellationRequested();
            return result;
        }, token);
}
