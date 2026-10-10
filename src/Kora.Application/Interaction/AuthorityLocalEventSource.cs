using Kora.Application.Maintenance;
using Kora.Application.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Application.Interaction;

public sealed class AuthorityLocalEventSource(
    ISessionWorkStore work, ISessionWorkspaceAccess access, MaintenanceViewModel maintenance,
    SessionQueueLimits limits, TimeProvider time, SessionQueueConfigurationService? configuration = null) : ILocalEventSource
{
    private Task<T> WithLimitsAsync<T>(Func<SessionQueueLimits, Task<T>> operation, CancellationToken token) =>
        configuration is null ? operation(limits) : configuration.WithLimitsAsync(operation, token);
    public async Task<IReadOnlyList<LocalEvent>> ReadAsync(HostId<SessionIdentity> session, CancellationToken token)
    {
        var revision = access.ControlRevision;
        RequireAdmission(revision);
        var snapshot = await WithLimitsAsync(current => work.ReadWorkAsync(session, revision, current, token).AsTask(), token).ConfigureAwait(false);
        snapshot.RequireSubject(session);
        var events = FromWork(snapshot, time.GetUtcNow());
        var release = BindMaintenance(maintenance.ReadLocalEvent(session), snapshot);
        RequireAdmission(revision);
        return release is null ? events : events.Append(release).ToArray();
    }

    public async Task<T> WithCurrentAsync<T>(HostId<SessionIdentity> session, IReadOnlyList<LocalEvent> expected,
        Func<T> observation, CancellationToken token)
    {
        var revision = access.ControlRevision;
        RequireAdmission(revision);
        return await WithLimitsAsync(currentLimits => work.WithCurrentWorkAsync(session, revision, currentLimits, snapshot =>
        {
            snapshot.RequireSubject(session);
            var events = FromWork(snapshot, time.GetUtcNow());
            return maintenance.WithLocalEvent(session, cached =>
            {
                var release = BindMaintenance(cached, snapshot);
                IReadOnlyList<LocalEvent> current = release is null ? events : [.. events, release];
                if (current.Count != expected.Count || current.Any(item => !expected.Any(item.SameSource)))
                { throw new InvalidOperationException("The authoritative event source, revision, deadline or profile changed."); }
                token.ThrowIfCancellationRequested();
                RequireAdmission(revision);
                if (configuration is not null && !configuration.IsCurrent(currentLimits))
                { throw new InvalidOperationException("Queue configuration changed before local event presentation."); }
                var result = observation();
                token.ThrowIfCancellationRequested();
                RequireAdmission(revision);
                if (current.Any(item => item.ExpiresAt <= time.GetUtcNow() || item.ObservedAt > time.GetUtcNow()))
                { throw new InvalidOperationException("The local event deadline or clock changed before presentation."); }
                return result;
            });
        }, token).AsTask(), token).ConfigureAwait(false);
    }

    private static LocalEvent? BindMaintenance(LocalEvent? cached, SessionWorkSnapshot snapshot) =>
        !snapshot.Session.Authority.IsActive || cached is null ? null
            : cached with { RelatedRevision = cached.Generation, Generation = snapshot.Session.Authority.Generation.Value };

    private void RequireAdmission(long revision)
    {
        if (!access.CanControl || access.ControlRevision != revision)
        { throw new InvalidOperationException("Local event observation needs unchanged owning private unlocked host/call admission."); }
    }

    internal static IReadOnlyList<LocalEvent> FromWork(SessionWorkSnapshot snapshot, DateTimeOffset now)
    {
        var session = snapshot.Session.Authority;
        if (!session.IsActive) { return []; }
        var events = new List<LocalEvent>();
        foreach (var row in snapshot.QueueRecords)
        {
            var entry = row.Entry;
            entry.Validate();
            if (entry.Request.SessionId != session.SessionId || entry.Generation != session.Generation)
            { continue; }
            LocalEventType? type = (entry.State, row.Eligibility) switch
            {
                (SessionQueueState.Unknown, _) => LocalEventType.Unknown,
                (SessionQueueState.Failed, _) => LocalEventType.Failed,
                (SessionQueueState.Succeeded, _) => LocalEventType.Completed,
                (SessionQueueState.Running, SessionQueueEligibility.Current) => LocalEventType.Current,
                (SessionQueueState.Pending, SessionQueueEligibility.Ready) => LocalEventType.Queued,
                (SessionQueueState.Pending, SessionQueueEligibility.DependencyNotSucceeded
                    or SessionQueueEligibility.UnclassifiedWorkOrWait or SessionQueueEligibility.UnknownQuarantine) => LocalEventType.Blocked,
                _ => null,
            };
            if (type is null || entry.ExpiresAt <= now || entry.EnqueuedAt > now) { continue; }
            events.Add(new(LocalEvent.Identity(LocalEventSource.LocalVersionQueue, session.SessionId.Value, entry.Request.TaskId.Value),
                1, LocalEventSource.LocalVersionQueue, type.Value, session.SessionId, entry.Request.TaskId,
                entry.Request.TaskId.Value, session.Generation.Value, entry.Revision.Value, 0, entry.Request.Origin,
                row.Eligibility, now, entry.ExpiresAt));
        }
        foreach (var question in snapshot.PendingQuestions)
        {
            var task = snapshot.Tasks.SingleOrDefault(item => item.Task.Request.TaskId == question.Key.Request.TaskId);
            if (question.Status != QuestionStatus.Pending || question.ExpiresAt <= now
                || question.Generation != session.Generation || question.Key.Request.SessionId != session.SessionId
                || task is not { CurrentSource: true, Source: LocalVersionWait.Source, Task.State: HostTaskState.IntentRecorded }
                || task.Question?.Key != question.Key
                || question.Key.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
            { continue; }
            events.Add(new(LocalEvent.Identity(LocalEventSource.LocalVersionQuestion, session.SessionId.Value, question.Key.QuestionId.Value),
                1, LocalEventSource.LocalVersionQuestion, LocalEventType.UserAttention, session.SessionId, question.Key.Request.TaskId,
                question.Key.QuestionId.Value, session.Generation.Value, question.Key.Revision.Value,
                task.Task.Revision.Value, question.Key.Request.Origin, null, now, question.ExpiresAt));
        }
        foreach (var item in events) { item.Validate(); }
        return events;
    }
}
