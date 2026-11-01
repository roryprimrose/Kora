using System.Collections.Immutable;
using System.Text.Json;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : ISessionAnswerReviewStore
{
    private sealed record AnswerReview(HostId<SessionIdentity> SessionId, HostId<RequestIdentity> RequestId,
        HostRevision Generation, Guid TurnId, long MessageRevision, long Revision, bool Reviewed);

    private void MigrateAnswerReviews(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateAuthority(connection);
        token.ThrowIfCancellationRequested();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM security_audit_events a,json_each(a.envelope,'$.Changes') c
            WHERE json_extract(c.value,'$.Kind')='answer-review' LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        { throw new InvalidDataException("Missing audited answer-review authority cannot be migrated as not marked."); }
        Execute(connection, transaction, HostInteractionSchema.AnswerReviewTable);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    public ValueTask<ConversationAnswerReviewPage> ReadAnswerReviewsAsync(SessionWorkspaceEntry target,
        ConversationAnswerReviewFilter filter, ConversationAnswerReviewCursor? cursor, int limit, CancellationToken token)
    {
        if (!Enum.IsDefined(filter)) { throw new ArgumentOutOfRangeException(nameof(filter)); }
        if (limit is < 1 or > 50) { throw new ArgumentOutOfRangeException(nameof(limit)); }
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            using var transaction = connection.BeginTransaction();
            var snapshot = ReadAnswerReviewSnapshot(connection, target, token);
            if (cursor is not null && (cursor.Target != target || cursor.Filter != filter
                || cursor.HeadRevision != snapshot.Head || cursor.ReviewRevision != snapshot.ReviewHead
                || cursor.After <= 0 || cursor.After >= snapshot.Messages.Count))
            { throw new InvalidOperationException("The exact answer-review query changed. Read again; no continuation was substituted."); }
            var answers = snapshot.Messages.Where(message => message.AssistantResponse is not null)
                .Select(message => ReviewRow(message, snapshot.Reviews.GetValueOrDefault(message.Request.RequestId))).ToArray();
            var remaining = answers.Count(row => !row.MarkedReviewed);
            var matches = answers.Where(row => row.Sequence > (cursor?.After ?? 0)
                && (filter == ConversationAnswerReviewFilter.All || !row.MarkedReviewed)).ToArray();
            var rows = matches.Take(limit).ToImmutableArray();
            var next = matches.Length > rows.Length
                ? new ConversationAnswerReviewCursor(target, filter, snapshot.Head, snapshot.ReviewHead, rows[^1].Sequence) : null;
            var page = new ConversationAnswerReviewPage(target, filter, snapshot.Head, snapshot.ReviewHead,
                answers.Length, remaining, rows, next);
            if (JsonSerializer.SerializeToUtf8Bytes(page).Length > ConversationText.MaximumPageUtf8Bytes)
            { throw new InvalidDataException("The complete answer-review metadata page exceeds the existing output budget."); }
            database.VerifyFiles();
            token.ThrowIfCancellationRequested();
            return page;
        }, token));
    }

    public ValueTask<ConversationMessage> ReadAnswerReviewMessageAsync(ConversationAnswerReviewPage page,
        ConversationAnswerReviewRow row, CancellationToken token) => new(Task.Run(() =>
    {
        using var lease = database.AcquireReadLease(token);
        using var connection = Open(created: false, token);
        using var transaction = connection.BeginTransaction();
        var message = RequireAnswerReviewSource(connection, page, row, token);
        database.VerifyFiles();
        token.ThrowIfCancellationRequested();
        return message;
    }, token));

    public async ValueTask<ConversationAnswerReviewRow> SetAnswerReviewedAsync(HostRequest control,
        ConversationAnswerReviewPage page, ConversationAnswerReviewRow row, bool reviewed,
        Func<bool> admitted, CancellationToken token)
    {
        var saved = await RunHostMutationAsync(control, "session.answer-review.set",
            (connection, transaction, intent, audit) =>
            {
                RequireFreshMetadataIntent(intent);
                if (control.SessionId != page.Target.Authority.SessionId || control.InvocationId is not null
                    || control.Origin != RequestOrigin.LocalUi || !admitted() || row.MarkedReviewed == reviewed)
                { throw new InvalidOperationException("Answer acknowledgement requires a deliberate exact native choice and unchanged private control."); }
                var message = RequireAnswerReviewSource(connection, page, row, token);
                var next = new AnswerReview(control.SessionId, row.RequestId, message.Generation, row.TurnId,
                    row.MessageRevision, checked(row.ReviewRevision + 1), reviewed);
                var terminal = intent.Next(HostTaskState.Succeeded);
                var sequence = AppendAudit(connection, transaction, intent, page.Target.Authority, audit,
                    changes: [AnswerReviewChange(next), TaskChange(terminal)], requestedAudit: ConversationRequestedAudit(audit));
                Execute(connection, transaction, """
                    INSERT INTO conversation_answer_reviews VALUES($request,$session,$revision,$payload,$audit)
                    ON CONFLICT(request_id) DO UPDATE SET revision=excluded.revision,payload=excluded.payload,
                        audit_sequence=excluded.audit_sequence;
                    """, ("$request", Id(next.RequestId)), ("$session", Id(next.SessionId)),
                    ("$revision", next.Revision), ("$payload", HostInteractionCodec.Encode(next)), ("$audit", sequence));
                WindowsSqliteHostTaskStore.WriteTask(connection, transaction, terminal);
                return ReviewRow(message, next);
            }, token, admitted, requireIdle: false).ConfigureAwait(false);
        var readback = await ReadAnswerReviewMessageStateAsync(page, row.RequestId, token).ConfigureAwait(false);
        if (!admitted() || readback != saved)
        { throw new InvalidDataException("Answer acknowledgement readback is unconfirmed. State is unavailable; inspect again explicitly."); }
        return saved;
    }

    private ValueTask<ConversationAnswerReviewRow> ReadAnswerReviewMessageStateAsync(ConversationAnswerReviewPage page,
        HostId<RequestIdentity> request, CancellationToken token) => new(Task.Run(() =>
    {
        using var lease = database.AcquireReadLease(token);
        using var connection = Open(created: false, token);
        using var transaction = connection.BeginTransaction();
        var snapshot = ReadAnswerReviewSnapshot(connection, page.Target, token);
        if (snapshot.Head != page.HeadRevision || snapshot.ReviewHead != checked(page.ReviewRevision + 1))
        { throw new InvalidDataException("Answer-review source changed during durable readback; state is unavailable until an explicit fresh read."); }
        var message = snapshot.Messages.Single(value => value.Request.RequestId == request);
        database.VerifyFiles();
        token.ThrowIfCancellationRequested();
        return ReviewRow(message, snapshot.Reviews.GetValueOrDefault(request));
    }, token));

    private static ConversationMessage RequireAnswerReviewSource(SqliteConnection connection,
        ConversationAnswerReviewPage page, ConversationAnswerReviewRow row, CancellationToken token)
    {
        var snapshot = ReadAnswerReviewSnapshot(connection, page.Target, token);
        var message = snapshot.Messages.SingleOrDefault(value => value.Request.RequestId == row.RequestId);
        if (!page.Rows.Contains(row) || page.HeadRevision != snapshot.Head || page.ReviewRevision != snapshot.ReviewHead
            || message?.AssistantResponse is null
            || row != ReviewRow(message, snapshot.Reviews.GetValueOrDefault(row.RequestId)))
        { throw new InvalidOperationException("The selected immutable answer or review snapshot changed. Read again; no newer source was substituted."); }
        return message;
    }

    private static (List<ConversationMessage> Messages, Dictionary<HostId<RequestIdentity>, AnswerReview> Reviews,
        long Head, long ReviewHead) ReadAnswerReviewSnapshot(SqliteConnection connection, SessionWorkspaceEntry target,
        CancellationToken token)
    {
        var session = RequireSession(connection, target.Authority.SessionId);
        if (session.State == 2 || target != new SessionWorkspaceEntry(session.Authority, ReadMetadata(connection, session.Authority.SessionId)))
        { throw new InvalidOperationException("The exact retained conversation generation or metadata changed or was removed."); }
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload,audit_sequence FROM conversation_messages WHERE session_id=$id ORDER BY sequence;
            """;
        command.Parameters.AddWithValue("$id", Id(target.Authority.SessionId));
        var messages = new List<ConversationMessage>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                var message = DecodeConversationMessage(connection, reader.GetString(0), reader.GetInt64(1));
                if (message.Request.SessionId != target.Authority.SessionId
                    || message.Generation.Value > target.Authority.Generation.Value
                    || message.Sequence != messages.Count + 1 || messages.Count >= ConversationText.MaximumSessionMessages)
                { throw new InvalidDataException("The complete canonical answer-review conversation source is inconsistent."); }
                messages.Add(message);
            }
        }
        var reviews = new Dictionary<HostId<RequestIdentity>, AnswerReview>();
        command.CommandText = "SELECT request_id,revision,payload,audit_sequence FROM conversation_answer_reviews WHERE session_id=$id;";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                var review = DecodeAnswerReview(connection, reader, target.Authority.SessionId);
                reviews.Add(review.RequestId, review);
            }
        }
        return (messages, reviews, messages.Sum(message => message.Revision), reviews.Values.Sum(review => review.Revision));
    }

    private static ConversationAnswerReviewRow ReviewRow(ConversationMessage message, AnswerReview? review) =>
        new(message.Request.RequestId, message.AssistantResponse!.Provenance.Turn.Value, message.Sequence,
            message.Revision, message.SubmittedAt, review?.Revision ?? 0, review?.Reviewed ?? false);

    private static AuthorityChange AnswerReviewChange(AnswerReview review) =>
        Change("answer-review", Id(review.RequestId), review.Revision, HostInteractionCodec.Encode(review));

    private static AnswerReview DecodeAnswerReview(SqliteConnection connection, SqliteDataReader reader,
        HostId<SessionIdentity> session)
    {
        var payload = reader.GetString(2);
        var review = HostInteractionCodec.Decode<AnswerReview>(payload);
        var message = ReadConversationMessage(connection, review.RequestId);
        if (review.SessionId != session || !Same(Id(review.RequestId), reader.GetString(0)) || review.Revision <= 0
            || review.Revision != reader.GetInt64(1) || message?.AssistantResponse is null
            || message.Request.SessionId != session || review.Generation != message.Generation
            || review.TurnId != message.AssistantResponse.Provenance.Turn.Value || review.MessageRevision != message.Revision
            || !Same(payload, HostInteractionCodec.Encode(review)))
        { throw new InvalidDataException("Answer-review metadata is malformed or has lost its exact immutable source."); }
        ValidateRowAuthority(connection, reader.GetInt64(3), AnswerReviewChange(review));
        return review;
    }

    private static void ValidateAnswerReviews(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT request_id,revision,payload,audit_sequence,session_id FROM conversation_answer_reviews;";
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            { _ = DecodeAnswerReview(connection, reader, new(ParseId(reader.GetString(4)))); }
        }
        command.CommandText = """
            SELECT 1 FROM (
                SELECT json_extract(c.value,'$.Id') AS id, MAX(a.sequence) AS sequence
                FROM security_audit_events a,json_each(a.envelope,'$.Changes') c
                JOIN conversation_messages m ON m.request_id=json_extract(c.value,'$.Id')
                WHERE json_extract(c.value,'$.Kind')='answer-review'
                GROUP BY json_extract(c.value,'$.Id')
            ) latest LEFT JOIN conversation_answer_reviews r ON r.request_id=latest.id
            WHERE r.audit_sequence IS NULL OR r.audit_sequence<>latest.sequence LIMIT 1;
            """;
        if (command.ExecuteScalar() is not null)
        { throw new InvalidDataException("Committed answer-review state is missing or stale, not confirmed absence."); }
    }
}
