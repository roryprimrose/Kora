using System.Security.Cryptography;

using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    private sealed record LocalHandoffObservation(Guid Id, ModelTurnProvenance Execution, HostRevision TaskRevision,
        HostQuestionKey Question, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string ContextDigest,
        ProviderProfileTuple Profile, ModelProviderSelection Destination, long ProviderRevision, string ProviderPolicy);

    public ValueTask<HostQuestionRecord> CreateVerifiedLocalHandoffAsync(LocalUnavailableObservation observation,
        ModelContextEnvelope context, ProviderProfileTuple profile, ModelProviderSelection destination,
        SessionProviderProfile providerState, Func<bool> admitted, CancellationToken token) =>
        RunHostMutationAsync(context.Request, "authority.snapshot", (connection, transaction, task, audit) =>
        {
            var source = RequireLocalUnavailableSource(connection, task, observation, context);
            RequireHandoffProviderState(connection, task.Request.SessionId, providerState);
            RequireDispositionIdle(connection, task.Request.SessionId, task.Request.TaskId);
            var session = RequireSession(connection, task.Request.SessionId).Authority;
            var previous = ReadObservation(connection, task.Request.RequestId);
            var gate = livePolicyGate
                ?? throw new InvalidOperationException("The owning private host policy is unavailable for local handoff review.");
            return gate.WithCurrent(previous?.Policy ?? new(false, false, false, true), policy =>
            {
                if (!policy.IsUnlocked || !admitted() || !observation.IsCurrent)
                { throw new InvalidOperationException("Local handoff review requires current original private host authority."); }
                var question = new HostQuestionRecord(new(task.Request, new(Guid.NewGuid()), new(1)),
                    LocalHandoffQuestion(), session.Generation, context.ExpiresAt);
                var recorded = new Observation(task.Request, runId,
                    new(checked((previous?.Revision.Value ?? 0) + 1)), session.Generation, policy, null)
                {
                    LocalHandoff = new(observation.Id, source.Execution!, task.Revision, question.Key,
                        context.CreatedAt, context.ExpiresAt, ContextDigest(context), profile, destination,
                        providerState.Revision, providerState.Desired.Serialize()),
                };
                var sequence = AppendAudit(connection, transaction, task, session, audit,
                    changes: [ObservationChange(recorded), QuestionChange(question)]);
                WriteObservation(connection, transaction, recorded, sequence);
                WriteQuestion(connection, transaction, question, sequence);
                return question;
            });
        }, token, () => admitted() && observation.IsCurrent, requireIdle: false);

    public ValueTask<ConversationHandoffCommit> CommitVerifiedLocalHandoffAsync(LocalUnavailableObservation observation,
        HostQuestionRecord review, ModelContextEnvelope context, ModelContextEnvelope successor,
        ProviderProfileTuple profile, ModelProviderSelection destination, SessionProviderProfile providerState,
        Func<bool> admitted, CancellationToken token) =>
        RunHostMutationAsync(context.Request, "conversation.answer", (connection, transaction, task, audit) =>
        {
            var source = RequireLocalUnavailableSource(connection, task, observation, context);
            RequireHandoffProviderState(connection, task.Request.SessionId, providerState);
            var session = RequireSession(connection, task.Request.SessionId).Authority;
            var recorded = ReadObservation(connection, task.Request.RequestId);
            var question = ReadQuestions(connection, task.Request.SessionId)
                .SingleOrDefault(item => item.Key.QuestionId == review.Key.QuestionId);
            var binding = recorded?.LocalHandoff;
            if (recorded?.RunId != runId || recorded.Generation != session.Generation
                || binding is null || binding.Id != observation.Id || binding.Execution != source.Execution
                || binding.TaskRevision != task.Revision || binding.Question != review.Key
                || binding.CreatedAt != context.CreatedAt || binding.ExpiresAt != context.ExpiresAt
                || binding.Profile != profile || binding.Destination != destination
                || binding.ProviderRevision != providerState.Revision
                || !Same(binding.ProviderPolicy, providerState.Desired.Serialize())
                || !Same(binding.ContextDigest, ContextDigest(context))
                || !SameQuestionSpec(review.Spec, LocalHandoffQuestion())
                || question is null || question.Key != review.Key.Next() || question.Status != QuestionStatus.Answered
                || question.Proposal is not null || question.SessionGeneration != session.Generation
                || question.ExpiresAt != context.ExpiresAt || !SameQuestionSpec(question.Spec, review.Spec)
                || question.Draft is not { } answer || !answer.Choices.SequenceEqual(["approve"])
                || answer.Text is not null || question.AnswerChannel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
            { throw new InvalidOperationException("Only the exact current answered one-use local handoff review may create a successor."); }
            RequireDispositionIdle(connection, task.Request.SessionId, task.Request.TaskId);
            var request = successor.Request;
            var rebound = RebindHandoffContext(context, request);
            if (request.SessionId != task.Request.SessionId || request.Origin != task.Request.Origin
                || request.InvocationId is not null || request.RequestId == task.Request.RequestId
                || request.TaskId == task.Request.TaskId || successor.CreatedAt != context.CreatedAt
                || successor.ExpiresAt != context.ExpiresAt
                || !successor.Serialize().AsSpan().SequenceEqual(rebound.Serialize())
                || ReadTask(connection, request.TaskId) is not null
                || ReadConversationMessage(connection, request.RequestId) is not null)
            { throw new InvalidOperationException("A handoff requires one fresh exact original-session successor without renewed context or authority."); }
            var count = Scalar(connection, "SELECT count(*) FROM conversation_messages WHERE session_id=$id;", request.SessionId);
            if (count >= ConversationText.MaximumSessionMessages) { throw new ConversationCapacityException(); }
            var failure = source with
            {
                Revision = 3, Outcome = ModelTurnOutcome.Unavailable, Reason = observation.Result.Reason,
            };
            failure.Validate();
            var terminal = task.Next(failure.ExpectedTaskState);
            var terminalAudit = audit.WithOutcome(SecurityAuditOutcome.Denied);
            var sequence = AppendAudit(connection, transaction, task, session, terminalAudit,
                changes: [MessageChange(failure), TaskChange(terminal)], requestedAudit: ConversationRequestedAudit(terminalAudit));
            WriteConversationMessage(connection, transaction, failure, sequence);
            WindowsSqliteHostTaskStore.WriteTask(connection, transaction, terminal);
            var intent = new HostTaskRecord(request, new(1), HostTaskState.IntentRecorded);
            var message = new ConversationMessage(request, session.Generation, count + 1, 1, time.GetUtcNow(),
                source.Body, null, null)
            {
                Handoff = new(observation.Id, source.Execution!, task.Revision, review.Key,
                    context.CreatedAt, context.ExpiresAt, ContextDigest(successor), profile, destination),
            };
            message.Validate();
            var submission = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
                "conversation.submit", SecurityAuditOutcome.Succeeded, audit.Initiator, Id(request.TaskId));
            using (var successorActivity = HostActivity.BeginAudit(request, submission,
                [new(HostActivity.RequireCurrent().Activity!.Context)]))
            {
                WindowsSqliteHostTaskStore.WriteTask(connection, transaction, intent);
                Execute(connection, transaction, "INSERT INTO host_task_runs VALUES($task,$run);",
                    ("$task", Id(request.TaskId)), ("$run", runId.ToString("D")));
                var successorSequence = AppendAudit(connection, transaction, intent, session, submission,
                    changes: [MessageChange(message), TaskChange(intent)], requestedAudit: ConversationRequestedAudit(submission));
                WriteConversationMessage(connection, transaction, message, successorSequence);
                successorActivity.Complete(HostOperationOutcome.Completed);
            }
            TouchActivity(connection, transaction, request.SessionId);
            return new ConversationHandoffCommit(new(failure, terminal), new(message, intent));
        }, token, () => admitted() && observation.IsCurrent, requireIdle: false);

    private ConversationMessage RequireLocalUnavailableSource(SqliteConnection connection, HostTaskRecord task,
        LocalUnavailableObservation observation, ModelContextEnvelope context)
    {
        var source = ReadConversationMessage(connection, task.Request.RequestId);
        var session = RequireSession(connection, task.Request.SessionId);
        if (!observation.IsCurrent || !observation.Admits(context) || source is null
            || observation.Result is not { Outcome: ModelTurnOutcome.Unavailable, Response: null, Provenance: { } provenance }
            || provenance.Selection.Provider != ModelProviderIdentity.Ollama || provenance != source.Execution
            || provenance.Request != task.Request || context.Request != task.Request
            || !Same(source.Body, observation.Context.UserRequest)
            || source.Request != task.Request || source.Version != 2 || source.Revision != 2 || source.Outcome is not null
            || task.State != HostTaskState.DispatchRecorded || task.Revision != observation.TaskRevision
            || task.Revision.Value != 2 || session.State != 0 || session.Authority.Generation != observation.Generation
            || source.Generation != observation.Generation || ReadRun(connection, task.Request.TaskId) != runId
            || (ReadMetadata(connection, task.Request.SessionId)?.Revision.Value ?? 0) != source.ExecutionMetadataRevision
            || time.GetUtcNow() < context.CreatedAt || time.GetUtcNow() >= context.ExpiresAt
            || context.Evidence.Any(item => item.Disclosure != ModelEvidenceDisclosure.HostedEligible))
        { throw new InvalidOperationException("An exact current local Unavailable result and confirmed owned physical join are required."); }
        context.RequireCompleteInputLimit();
        return source;
    }

    private static QuestionSpec LocalHandoffQuestion() => new(
        "Review the exact context envelope and Copilot destination before confirming this new hosted turn. "
        + "Confirmation does not qualify the runtime or permit network transmission.",
        QuestionKind.SingleChoice, [new("approve", "Approve exact envelope"), new("decline", "Decline")],
        purpose: "provider-handoff", sourceId: "host");

    private static void RequireHandoffProviderState(SqliteConnection connection, HostId<SessionIdentity> session,
        SessionProviderProfile expected)
    {
        var current = ReadProviderProfileRow(connection, session);
        if (expected.Held || current.Held || expected.Session != current.Session
            || expected.Revision != current.Revision || !Same(expected.Desired.Serialize(), current.Desired.Serialize()))
        { throw new InvalidOperationException("The exact session provider policy changed before handoff commit."); }
    }

    private static bool SameQuestionSpec(QuestionSpec left, QuestionSpec right) =>
        left.Kind == right.Kind && Same(left.Text, right.Text) && Same(left.Purpose, right.Purpose)
        && Same(left.SourceId, right.SourceId) && left.Minimum == right.Minimum && left.Maximum == right.Maximum
        && left.MaximumTextLength == right.MaximumTextLength && left.Options.SequenceEqual(right.Options);

    private static ModelContextEnvelope RebindHandoffContext(ModelContextEnvelope context, HostRequest request) =>
        new(request, context.SystemPolicy, context.UserRequest,
            context.Evidence.Select(item => item with { Request = request }), context.CreatedAt, context.ExpiresAt,
            context.ToolMode, context.Constraints, context.McpTool, context.WebPages);

    private static string ContextDigest(ModelContextEnvelope context) =>
        Convert.ToHexStringLower(SHA256.HashData(context.Serialize()));

    private static AuthorityChange ObservationChange(Observation observation) =>
        Change("observation", Id(observation.Request.RequestId), observation.Revision.Value, HostInteractionCodec.Encode(observation));

    private static void WriteObservation(SqliteConnection connection, SqliteTransaction transaction, Observation observation, long sequence) =>
        Execute(connection, transaction, """
            INSERT INTO host_observations(request_id,session_id,run_id,revision,generation,payload,audit_sequence)
            VALUES($id,$session,$run,$revision,$generation,$payload,$audit)
            ON CONFLICT(request_id) DO UPDATE SET run_id=excluded.run_id,revision=excluded.revision,
                generation=excluded.generation,payload=excluded.payload,audit_sequence=excluded.audit_sequence;
            """, ("$id", Id(observation.Request.RequestId)), ("$session", Id(observation.Request.SessionId)),
            ("$run", observation.RunId.ToString("D")), ("$revision", observation.Revision.Value),
            ("$generation", observation.Generation.Value), ("$payload", HostInteractionCodec.Encode(observation)), ("$audit", sequence));
}
