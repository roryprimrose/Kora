using System.Security.Cryptography;
using System.Text;

using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Presentation;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : IDetailsOfferStore
{
    private static bool IsDetailsQuestionAction(string action) => action is "question.submit" or "question.voice-submit"
        or "question.voice-review" or "question.cancel" or "question.review" or "details.offer.read";
    private static bool IsDetailsAuditAction(string action) => IsDetailsQuestionAction(action)
        || action is "details.offer.admit" or "details.offer.complete" or "details.offer.retire";
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Func<bool>> detailsOfferAdmissions = new();
    private static System.Collections.Immutable.ImmutableArray<HostQuestionRecord> ReadDetailsOfferQuestions(
        SqliteConnection connection, HostId<SessionIdentity> session)
    {
        const int maximum = Kora.Core.Conversation.ConversationText.MaximumSessionMessages * 3;
        var records = ReadQuestions(connection, session, limit: maximum + 1);
        if (records.Length > maximum)
        { throw new InvalidDataException("This session exceeds the bounded complete Details-question inventory."); }
        return records;
    }
    private void RetireDetailsOffersBeforeLifecycle(SqliteConnection connection, HostId<SessionIdentity> session)
    {
        using var transaction = connection.BeginTransaction();
        RetireDetailsOffers(connection, transaction, session);
        transaction.Commit();
    }
    private void RetireDetailsOffers(SqliteConnection connection, SqliteTransaction transaction,
        HostId<SessionIdentity>? session, bool previousRunOnly = false)
    {
        foreach (var original in ReadQuestions(connection, session))
        {
            if (original.DetailsOffer is not { } binding || previousRunOnly && binding.RunId == runId)
            { continue; }
            var task = ReadTask(connection, original.Key.Request.TaskId)
                ?? throw new InvalidDataException("A Details question has no owning presentation task.");
            if (task.IsTerminal && original.Status != QuestionStatus.Pending) { continue; }
            detailsOfferAdmissions.TryRemove(task.Request.TaskId.Value, out _);
            using var host = HostActivity.BeginRoot(original.Key.Request, HostActivityLayer.Windows, HostOperation.Presentation);
            var question = original.Status == QuestionStatus.Pending ? original with
            {
                Key = original.Key.Next(),
                Status = time.GetUtcNow() >= original.ExpiresAt ? QuestionStatus.Expired : QuestionStatus.Cancelled,
            } : original;
            var terminal = task.IsTerminal ? task : task.Next(previousRunOnly ? HostTaskState.Interrupted : HostTaskState.Cancelled);
            var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
                "details.offer.retire", SecurityAuditOutcome.Cancelled, SecurityAuditInitiator.System, Id(task.Request.TaskId));
            using var auditActivity = HostActivity.BeginAudit(task.Request, audit);
            var changes = new List<AuthorityChange>();
            if (question != original) { changes.Add(QuestionChange(question)); }
            if (terminal != task) { changes.Add(TaskChange(terminal)); }
            var sequence = AppendAudit(connection, transaction, task, RequireSession(connection, task.Request.SessionId).Authority,
                audit, new(HostInteractionOutcome.Cancelled, "details-offer-withdrawn", question), changes);
            if (question != original) { WriteQuestion(connection, transaction, question, sequence); }
            if (terminal != task) { WindowsSqliteHostTaskStore.WriteTask(connection, transaction, terminal); }
            auditActivity.Complete(HostOperationOutcome.Completed);
            host.Complete(HostOperationOutcome.Completed);
        }
    }

    private ValueTask WithdrawDetailsOffersAsync(HostId<SessionIdentity> session, HostRevision generation, CancellationToken token) =>
        new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            if (RequireSession(connection, session).Authority.Generation != generation)
            { throw new InvalidOperationException("The exact session generation changed before presentation withdrawal."); }
            using var transaction = connection.BeginTransaction();
            RetireDetailsOffers(connection, transaction, session);
            token.ThrowIfCancellationRequested();
            transaction.Commit();
            database.VerifyFiles();
        }, token));

    public ValueTask<HostQuestionRecord> AdmitDetailsOfferAsync(DetailsOfferBinding source,
        Func<bool> admitted, CancellationToken token)
    {
        var request = HostActivity.RequireCurrent().Request;
        if (request.Origin != RequestOrigin.HostSystem || request.InvocationId is not null)
        { throw new InvalidOperationException("Details offers require a separate host-owned presentation request."); }
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            using var transaction = connection.BeginTransaction();
            var binding = source with { RunId = runId };
            var retention = ReadRetention(connection, source.OriginalRequest.SessionId);
            if (!retention.Perpetual)
            {
                var expires = new[] { binding.ExpiresAt, retention.ArchiveDue, retention.DeleteDue }.Min();
                binding = binding with { ExpiresAt = expires };
            }
            var question = new HostQuestionRecord(new(request, new(Guid.NewGuid()), new(1)),
                DetailsOfferBinding.CreateSpec(), source.Generation, binding.ExpiresAt)
            { DetailsOffer = binding };
            binding.Validate(question);
            if (!admitted() || ReadTask(connection, request.TaskId) is not null
                || ReadDetailsOfferQuestions(connection, request.SessionId).Any(q => q.Status == QuestionStatus.Pending
                    || q.DetailsOffer is { } previous && (previous.OriginalRequest == source.OriginalRequest
                        || ReadTask(connection, q.Key.Request.TaskId) is not { IsTerminal: true }))
                || !DetailsOfferSourceCurrent(connection, binding))
            { throw new InvalidOperationException("The fresh exact foreground Details offer is unavailable or already consumed."); }
            var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, intent);
            var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
                "details.offer.admit", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.System, Id(request.TaskId));
            using var activity = HostActivity.BeginAudit(request, audit);
            var decision = new HostInteractionDecision(HostInteractionOutcome.Presented, "details-offer-presented", question);
            var sequence = AppendAudit(connection, transaction, intent, RequireSession(connection, request.SessionId).Authority,
                audit, decision, [TaskChange(intent), QuestionChange(question)]);
            WriteQuestion(connection, transaction, question, sequence);
            Commit(transaction, request, token, admitted);
            if (!detailsOfferAdmissions.TryAdd(request.TaskId.Value, admitted))
            { throw new InvalidOperationException("The exact Details control already has a live owner."); }
            activity.Complete(HostOperationOutcome.Completed);
            return question;
        }, token));
    }

    private bool DetailsOfferSourceCurrent(SqliteConnection connection, DetailsOfferBinding source)
    {
        var session = RequireSession(connection, source.OriginalRequest.SessionId);
        var now = time.GetUtcNow();
        var retention = ReadRetention(connection, source.OriginalRequest.SessionId);
        var message = ReadConversationMessage(connection, source.OriginalRequest.RequestId);
        return source.RunId == runId && session.State == 0 && session.Authority.Generation == source.Generation
            && (ReadMetadata(connection, session.Authority.SessionId)?.Revision.Value ?? 0) == source.MetadataRevision
            && now >= source.CreatedAt && now < source.ExpiresAt
            && (retention.Perpetual || now < retention.ArchiveDue && now < retention.DeleteDue)
            && message is { AssistantResponse: { } answer } && message.Request == source.OriginalRequest
            && message.Sequence == source.Sequence && message.Revision == source.MessageRevision
            && message.Generation == source.Generation && answer.Provenance.Turn.Value == source.Item
            && string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(answer.Body))),
                source.SourceDigest, StringComparison.Ordinal)
            && ReadTask(connection, source.OriginalRequest.TaskId) is { State: HostTaskState.Succeeded }
            && !ReadDetailsOfferQuestions(connection, source.OriginalRequest.SessionId)
                .Any(q => q.Status == QuestionStatus.Pending && q.DetailsOffer is null);
    }

    private static HostQuestionRecord? DetailsOfferForTask(SqliteConnection connection, HostRequest request)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT question_id,request_id,session_id,revision,payload,audit_sequence FROM host_questions
            WHERE request_id=$request AND json_type(payload,'$.DetailsOffer') IS NOT NULL LIMIT 2;
            """;
        command.Parameters.AddWithValue("$request", Id(request.RequestId));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) { return null; }
        var question = ReadQuestionRow(connection, reader);
        if (question.Key.Request.TaskId != request.TaskId || question.Key.Request.SessionId != request.SessionId || reader.Read())
        { throw new InvalidDataException("The Details request has foreign or competing presentation authority."); }
        return question;
    }

    public async ValueTask CompleteDetailsOfferAsync(HostQuestionKey key, bool revealed,
        Func<bool> admitted, CancellationToken token)
    {
        if (!revealed) { detailsOfferAdmissions.TryRemove(key.Request.TaskId.Value, out _); }
        if (!revealed)
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            var existing = DetailsOfferForTask(connection, key.Request);
            if (existing?.Key.QuestionId == key.QuestionId
                && existing.Status != QuestionStatus.Pending && ReadTask(connection, key.Request.TaskId) is { IsTerminal: true })
            { return; }
        }
        await RunHostMutationAsync(key.Request, "details.offer.complete", (connection, transaction, intent, audit) =>
        {
            var question = DetailsOfferForTask(connection, key.Request)
                ?? throw new InvalidOperationException("The exact Details control is unavailable.");
            if (question.Key.QuestionId != key.QuestionId || question.Key.Revision.Value < key.Revision.Value)
            { throw new InvalidOperationException("The Details question identity changed."); }
            var opened = revealed && admitted() && DetailsOfferSourceCurrent(connection, question.DetailsOffer!)
                && question.Status == QuestionStatus.Answered && question.Draft!.Choices.SequenceEqual(["yes"]);
            if (revealed && !opened)
            { throw new InvalidOperationException("Details reveal was not confirmed for the current accepted source."); }
            var changed = question.Status == QuestionStatus.Pending;
            if (changed)
            {
                question = question with
                {
                    Key = question.Key.Next(),
                    Status = time.GetUtcNow() >= question.ExpiresAt
                    ? QuestionStatus.Expired : QuestionStatus.Cancelled
                };
            }
            var acceptedYes = question.Status == QuestionStatus.Answered && question.Draft!.Choices.SequenceEqual(["yes"]);
            var terminal = intent.Next(opened ? HostTaskState.Succeeded : acceptedYes ? HostTaskState.Failed : HostTaskState.Cancelled);
            var decision = new HostInteractionDecision(opened ? HostInteractionOutcome.Answered : HostInteractionOutcome.Cancelled,
                opened ? "details-offer-revealed" : "details-offer-retired", question);
            var terminalAudit = new SecurityAuditEvent(audit.CorrelationId, audit.Category, audit.ActionId,
                opened ? SecurityAuditOutcome.Succeeded : acceptedYes ? SecurityAuditOutcome.Unknown : SecurityAuditOutcome.Cancelled,
                audit.Initiator, audit.TargetId, reasonCode: opened ? "details-revealed" : acceptedYes ? "details-reveal-unconfirmed" : "details-retired");
            var sequence = AppendAudit(connection, transaction, intent, RequireSession(connection, key.Request.SessionId).Authority,
                terminalAudit, decision, changed ? [TaskChange(terminal), QuestionChange(question)] : [TaskChange(terminal)]);
            if (changed) { WriteQuestion(connection, transaction, question, sequence); }
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, terminal);
            return true;
        }, token, requireIdle: false).ConfigureAwait(false);
        detailsOfferAdmissions.TryRemove(key.Request.TaskId.Value, out _);
    }

    private bool DetailsOfferAdmissionCurrent(SqliteConnection connection, HostQuestionRecord question) =>
        detailsOfferAdmissions.TryGetValue(question.Key.Request.TaskId.Value, out var current) && current()
        && DetailsOfferSourceCurrent(connection, question.DetailsOffer!);
}