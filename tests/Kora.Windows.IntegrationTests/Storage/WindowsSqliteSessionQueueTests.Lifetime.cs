using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Application.Auditing;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionQueueTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task ConfirmedActiveSettingsHaveTrustedCorrelatedReceiptsAndCaptureFutureAdmissionOnly(
        RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var pending = await EnqueueAsync(f, f.Request.SessionId, minutes: 120);
        var pendingPayload = QueuePayload(f, pending);
        var observed = new LifetimeEvidence();
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        using var provider = new EvidenceLoggerProvider([sink, observed], observed);
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        var audit = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
        await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
        using var call = new CallCommunicationPolicy(new LifetimeClearCall());
        var preferences = new LocalSessionQueuePreferences(f.Paths);
        await using var settings = new SessionQueueConfigurationService(preferences, admission, audit);
        settings.Observe();
        SessionQueueEntry? previous = null;
        foreach (var budget in new[] { 1, 5, 60 })
        {
            await settings.RefreshAsync(origin, static () => true, f.Token);
            var proposal = settings.Propose(SessionQueueOption.ActiveBudgetMinutes, budget.ToString(CultureInfo.InvariantCulture),
                settings.Get().Revision, call.Current.Revision);
            (await settings.ApplyAsync(proposal, origin, initiator, call, static () => true, f.Token)).Should().BeTrue();
            if (previous is not null)
            {
                (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, previous.Request.TaskId, f.Token)).Should().Be(previous);
                pending = await EnqueueAsync(f, f.Request.SessionId, minutes: 120);
                pendingPayload = QueuePayload(f, pending);
            }
            QueuePayload(f, pending).Should().Be(pendingPayload);
            using var root = HostActivity.BeginRoot(pending.Request, HostActivityLayer.Application, HostOperation.Request);
            var receipt = await settings.WithLimitsAsync(limits =>
                f.Store.AdmitAsync(pending, 1, limits, () => settings.IsCurrent(limits), f.Token).AsTask(), f.Token);
            receipt.Entry.ActiveBudgetMinutes.Should().Be(budget);
            receipt.Entry.ActiveDeadlineAt.Should().Be(f.Time.Now.AddMinutes(budget));
            receipt.Entry.ExpiresAt.Should().Be(pending.ExpiresAt);
            previous = await CompleteAsync(f, receipt.Entry);
            await using var cold = new SessionQueueConfigurationService(new LocalSessionQueuePreferences(f.Paths), admission, audit);
            cold.Observe();
            cold.Get().Effective!.ActiveBudgetMinutes.Should().Be(budget);
        }
        await settings.RefreshAsync(origin, static () => true, f.Token);
        (await settings.ApplyAsync(settings.Propose(SessionQueueOption.ActiveBudgetMinutes, null,
            settings.Get().Revision, call.Current.Revision), origin, initiator, call, static () => true, f.Token)).Should().BeTrue();
        settings.Get().Effective!.ActiveBudgetMinutes.Should().Be(5);
        observed.Gaps.Should().BeEmpty();
        observed.Audits.Should().HaveCount(8).And.OnlyContain(item => item.Audit.ActionId == "configuration.queue-active-budget-minutes"
            && item.Audit.Initiator == initiator && item.Diagnostic.Host!.Origin == origin
            && item.Audit.CorrelationId == item.Diagnostic.Host.RequestId.Value
            && item.Diagnostic.Host.SessionId != f.Request.SessionId);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, previous!.Request.TaskId, f.Token)).Should().Be(previous);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task Genuine_configuration_receipts_capture_future_lifetimes_and_preserve_original_authority(
        RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var first = await EnqueueAsync(f, f.Request.SessionId);
        var cancelled = await EnqueueAsync(f, f.Request.SessionId);
        await RemoveAsync(f, cancelled, (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Revision);
        cancelled = (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, cancelled.Request.TaskId, f.Token))!;
        var oldHistory = await f.Store.ReadHistoryAsync(f.Request.SessionId, null, 50, f.Token);
        var observed = new LifetimeEvidence();
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        using var provider = new EvidenceLoggerProvider([sink, observed], observed);
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        var audit = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
        await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
        using var call = new CallCommunicationPolicy(new LifetimeClearCall());
        var preferences = new LocalSessionQueuePreferences(f.Paths);
        await using var settings = new SessionQueueConfigurationService(preferences, admission, audit);
        settings.Observe();
        using var hostile = new System.Diagnostics.Activity("untrusted.queue-correlation")
            .SetIdFormat(System.Diagnostics.ActivityIdFormat.W3C).Start();
        foreach (var minutes in new[] { 1, 120, 30 })
        {
            var metadata = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            f.Time.Now = f.Time.Now.AddSeconds(1);
            await settings.RefreshAsync(origin, static () => true, f.Token);
            var proposal = settings.Propose(SessionQueueOption.PendingLifetimeMinutes,
                minutes.ToString(CultureInfo.InvariantCulture), settings.Get().Revision, call.Current.Revision);
            (await settings.ApplyAsync(proposal, origin, initiator, call, static () => true, f.Token)).Should().BeTrue();
            (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token)).Should().Be(metadata);
            var entry = await settings.WithLimitsAsync(limits =>
                EnqueueAsync(f, f.Request.SessionId, minutes: limits.PendingLifetimeMinutes), f.Token);
            entry.RecordVersion.Should().Be(2);
            entry.PendingLifetimeMinutes.Should().Be(minutes);
            entry.ExpiresAt.Should().Be(entry.EnqueuedAt.AddMinutes(minutes));
            var encoded = QueuePayload(f, entry);
            encoded.Should().Be(HostInteractionCodec.Encode(entry));
            RequireQueueDigest(f, entry, encoded);
            (await f.Store.ReadQueueEntryAsync(first.Request.SessionId, first.Request.TaskId, f.Token)).Should().Be(first);
            (await f.Store.ReadQueueEntryAsync(cancelled.Request.SessionId, cancelled.Request.TaskId, f.Token)).Should().Be(cancelled);
            var work = await f.Store.ReadWorkAsync(first.Request.SessionId, 1, settings.Get().Effective!, f.Token);
            work.QueueRecords.Single(row => row.Entry.Request.TaskId == first.Request.TaskId).Entry.Should().Be(first);
            await using var cold = new SessionQueueConfigurationService(new LocalSessionQueuePreferences(f.Paths), admission, audit);
            cold.Observe();
            cold.Get().Effective!.PendingLifetimeMinutes.Should().Be(minutes);
        }
        await settings.RefreshAsync(origin, static () => true, f.Token);
        (await settings.ApplyAsync(settings.Propose(SessionQueueOption.PendingLifetimeMinutes, null,
            settings.Get().Revision, call.Current.Revision), origin, initiator, call, static () => true, f.Token)).Should().BeTrue();
        preferences.Load().IsDefault.Should().BeTrue();
        settings.Get().Effective!.PendingLifetimeMinutes.Should().Be(30);
        observed.Gaps.Should().BeEmpty();
        observed.Audits.Should().HaveCount(8).And.OnlyContain(item =>
            item.Audit.ActionId == "configuration.queue-pending-lifetime-minutes"
            && item.Audit.Initiator == initiator && item.Diagnostic.Host!.Origin == origin
            && item.Audit.CorrelationId == item.Diagnostic.Host.RequestId.Value
            && item.Diagnostic.Host.SessionId != f.Request.SessionId);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().Contain(grant);
        var history = await f.Store.ReadHistoryAsync(f.Request.SessionId, null, 50, f.Token);
        history.Records.Take(oldHistory.Records.Length).Should().BeEquivalentTo(oldHistory.Records,
            options => options.WithStrictOrdering());
        System.Diagnostics.Activity.Current.Should().BeSameAs(hostile);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(120)]
    public async Task Exact_expiry_edge_reopen_and_active_budget_use_recorded_not_current_lifetime(int minutes)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var entry = await EnqueueAsync(f, f.Request.SessionId, minutes: minutes);
        var payload = QueuePayload(f, entry);
        f.Time.Now = entry.ExpiresAt.AddTicks(-1);
        (await f.Store.FindReadyAsync(1, new(pendingLifetimeMinutes: minutes == 1 ? 120 : 1), f.Token)).Should().Be(entry);
        f.Time.Now = entry.ExpiresAt;
        (await f.Store.ReadQueueEntryAsync(entry.Request.SessionId, entry.Request.TaskId, f.Token))!.State.Should().Be(SessionQueueState.Expired);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
        QueuePayload(f, entry).Should().Be(payload);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var recovered = (await f.Store.ReadQueueEntryAsync(entry.Request.SessionId, entry.Request.TaskId, f.Token))!;
        recovered.State.Should().Be(SessionQueueState.Interrupted);
        recovered.ExpiresAt.Should().Be(entry.ExpiresAt);
        recovered.PendingLifetimeMinutes.Should().Be(minutes);
        QueuePayload(f, entry).Should().Be(payload);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
        var fresh = await EnqueueAsync(f, f.Request.SessionId, minutes: minutes);
        var running = await AdmitAsync(f, fresh);
        var work = await f.Store.ReadWorkAsync(f.Request.SessionId, 1, new(pendingLifetimeMinutes: 120), f.Token);
        work.QueueRecords.Single(row => row.Entry.Request.TaskId == running.Request.TaskId).ActiveDeadline
            .Should().Be(f.Time.Now.AddMinutes(5));
        await CompleteAsync(f, running);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, fresh.Request.TaskId, f.Token))!.ExpiresAt.Should().Be(fresh.ExpiresAt);
    }

    [Theory]
    [InlineData("json_set(payload,'$.RecordVersion',3)")]
    [InlineData("json_set(payload,'$.RecordVersion',1)")]
    [InlineData("json_set(payload,'$.PendingLifetimeMinutes',0)")]
    [InlineData("json_set(payload,'$.PendingLifetimeMinutes',121)")]
    [InlineData("json_set(payload,'$.PendingLifetimeMinutes',1)")]
    [InlineData("json_remove(payload,'$.RecordVersion','$.PendingLifetimeMinutes')")]
    [InlineData("json_set(payload,'$.RecordVersion',NULL,'$.PendingLifetimeMinutes',NULL)")]
    [InlineData("json_set(payload,'$.UnknownLifetime',30)")]
    public async Task Hostile_unknown_partial_downgraded_or_unbound_payload_is_rejected_without_replacement(string expression)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var entry = await EnqueueAsync(f, f.Request.SessionId);
        f.Mutate("UPDATE session_queue SET payload=" + expression + ";");
        var payload = QueuePayload(f, entry);
        var audits = f.Count("security_audit_events");
        f.Reopen();
        var reopen = () => f.Store.InitializeAsync(f.Token).AsTask();
        await reopen.Should().ThrowAsync<InvalidDataException>();
        QueuePayload(f, entry).Should().Be(payload);
        f.Count("security_audit_events").Should().Be(audits);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("commit")]
    [InlineData("cancel")]
    public async Task Captured_lifetime_enqueue_interruption_rolls_back_entire_private_transaction(string stage)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var original = await EnqueueAsync(f, f.Request.SessionId, minutes: 120);
        var originalPayload = QueuePayload(f, original);
        using var cancellation = new CancellationTokenSource();
        var armed = false;
        var checkpoint = new InteractionTransactionCheckpoint
        {
            Audit = (_, _) => { if (armed && stage is "audit") { throw new IOException("Unavailable required audit"); } },
            Commit = (_, _) =>
            {
                if (!armed) { return; }
                if (stage is "commit") { throw new IOException("Interrupted commit"); }
                if (stage is "cancel") { cancellation.Cancel(); }
            },
        };
        f.Reopen(checkpoint);
        await f.Store.InitializeAsync(f.Token);
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var queued = new HostRequest(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var control = new HostRequest(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(control, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(control, f.Token);
        var audits = f.Count("security_audit_events");
        var tasks = f.Count("host_tasks");
        armed = true;
        var enqueue = () => f.Store.EnqueueAsync(control, queued, new(1), snapshot.Revision, 1, null,
            new(pendingLifetimeMinutes: 1), static () => true, cancellation.Token).AsTask();
        await enqueue.Should().ThrowAsync<Exception>();
        f.Count("host_tasks").Should().Be(tasks);
        f.Count("security_audit_events").Should().Be(audits);
        QueuePayload(f, original).Should().Be(originalPayload);
        (await f.Store.ReadQueueEntryAsync(queued.SessionId, queued.TaskId, f.Token)).Should().BeNull();
    }

    [WindowsFact]
    public async Task Legacy_fixed_30_payload_and_original_change_digest_survive_mixed_format_reopen_and_new_revisions()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var grant = await f.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var current = await EnqueueAsync(f, f.Request.SessionId);
        var legacy = current with { RecordVersion = null, PendingLifetimeMinutes = null };
        var oldBytes = HostInteractionCodec.Encode(new LegacyQueuePayload(legacy.Request, legacy.Generation, legacy.Revision,
            legacy.Position, legacy.State, legacy.RunId, legacy.AdmissionRevision, legacy.EnqueuedAt, legacy.ExpiresAt,
            legacy.Dependency, legacy.DispatchOrder));
        HostInteractionCodec.Encode(legacy).Should().Be(oldBytes);
        StageKnownLegacyQueue(f, legacy, oldBytes);
        RequireQueueDigest(f, legacy, oldBytes);
        var auditBytes = AuditPayloads(f);
        (await f.Store.ReadQueueEntryAsync(legacy.Request.SessionId, legacy.Request.TaskId, f.Token)).Should().Be(legacy);
        var following = await EnqueueAsync(f, f.Request.SessionId, minutes: 1);
        QueuePayload(f, legacy).Should().Be(oldBytes);
        await RemoveAsync(f, legacy, (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Revision);
        var removed = (await f.Store.ReadQueueEntryAsync(legacy.Request.SessionId, legacy.Request.TaskId, f.Token))!;
        removed.PendingLifetimeMinutes.Should().BeNull();
        removed.RecordVersion.Should().BeNull();
        removed.ExpiresAt.Should().Be(legacy.ExpiresAt);
        var revisedBytes = QueuePayload(f, legacy);
        revisedBytes.Should().Be(HostInteractionCodec.Encode(removed)).And.NotContain("RecordVersion").And.NotContain("PendingLifetimeMinutes");
        RequireQueueDigest(f, removed, revisedBytes);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        AuditPayloads(f).Take(auditBytes.Length).Should().Equal(auditBytes);
        QueuePayload(f, legacy).Should().Be(revisedBytes);
        (await f.Store.ReadGrantsAsync(f.Token)).Should().Contain(grant);
        (await f.Store.ReadQueueEntryAsync(legacy.Request.SessionId, legacy.Request.TaskId, f.Token))!.ExpiresAt.Should().Be(legacy.ExpiresAt);
        (await f.Store.ReadQueueEntryAsync(following.Request.SessionId, following.Request.TaskId, f.Token))!.ExpiresAt.Should().Be(following.ExpiresAt);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
    }

    private static string QueuePayload(InteractionStorageFixture f, SessionQueueEntry entry)
    {
        using var connection = f.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM session_queue WHERE task_id=$task;";
        command.Parameters.AddWithValue("$task", entry.Request.TaskId.Value.ToString("D"));
        return (string)command.ExecuteScalar()!;
    }

    private static void RequireQueueDigest(InteractionStorageFixture f, SessionQueueEntry entry, string payload)
    {
        using var connection = f.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT json_extract(c.value,'$.Digest') FROM session_queue q
            JOIN security_audit_events a ON a.sequence=q.audit_sequence, json_each(a.envelope,'$.Changes') c
            WHERE q.task_id=$task AND json_extract(c.value,'$.Kind')='queue';
            """;
        command.Parameters.AddWithValue("$task", entry.Request.TaskId.Value.ToString("D"));
        command.ExecuteScalar().Should().Be(LifetimeDigest(payload));
    }

    private static string[] AuditPayloads(InteractionStorageFixture f)
    {
        using var connection = f.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events ORDER BY sequence;";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) { rows.Add(reader.GetString(0)); }
        return rows.ToArray();
    }

    private static void StageKnownLegacyQueue(InteractionStorageFixture f, SessionQueueEntry entry, string payload, bool latestOnly = false)
    {
        // Materialize the exact former serializer's known fixture and chain BEFORE preservation assertions.
        // Production never rewrites historical audit authority.
        var audits = AuditPayloads(f);
        using var connection = f.OpenRaw();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE session_queue SET payload=$payload WHERE task_id=$task;";
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$task", entry.Request.TaskId.Value.ToString("D"));
        command.ExecuteNonQuery();
        var previous = new string('0', 64);
        for (var index = 0; index < audits.Length; index++)
        {
            var envelope = JsonNode.Parse(audits[index])!;
            foreach (var change in envelope["Changes"]!.AsArray())
            {
                if (string.Equals(change!["Kind"]!.GetValue<string>(), "queue", StringComparison.Ordinal)
                    && string.Equals(change["Id"]!.GetValue<string>(), entry.Request.TaskId.Value.ToString("D"), StringComparison.Ordinal)
                    && (!latestOnly || change["Revision"]!.GetValue<long>() == entry.Revision.Value))
                { change["Digest"] = LifetimeDigest(payload); }
            }
            var encoded = envelope.ToJsonString();
            var sequence = index + 1;
            var hash = LifetimeDigest(sequence.ToString(CultureInfo.InvariantCulture) + "\n" + previous + "\n" + encoded);
            command.Parameters.Clear();
            command.CommandText = "UPDATE security_audit_events SET envelope=$payload,previous_hash=$previous,hash=$hash WHERE sequence=$sequence;";
            command.Parameters.AddWithValue("$payload", encoded);
            command.Parameters.AddWithValue("$previous", previous);
            command.Parameters.AddWithValue("$hash", hash);
            command.Parameters.AddWithValue("$sequence", sequence);
            command.ExecuteNonQuery();
            previous = hash;
        }
        command.Parameters.Clear();
        command.CommandText = "UPDATE authority_head SET hash=$hash;";
        command.Parameters.AddWithValue("$hash", previous);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static string LifetimeDigest(string payload) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    private sealed record LegacyQueuePayload(HostRequest Request, HostRevision Generation, HostRevision Revision,
        long Position, SessionQueueState State, Guid RunId, long AdmissionRevision, DateTimeOffset EnqueuedAt,
        DateTimeOffset ExpiresAt, HostId<TaskIdentity>? Dependency, long DispatchOrder)
    {
        public bool IsPending => State == SessionQueueState.Pending;
        public bool IsCurrent => State == SessionQueueState.Running;
    }

    private sealed class LifetimeClearCall : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }

    private sealed class LifetimeEvidence : IEvidenceSink, IEvidenceGapReporter
    {
        public string Name => "queue-lifetime-fixture";
        internal List<AuditEnvelope> Audits { get; } = [];
        internal List<EvidenceGap> Gaps { get; } = [];
        public void WriteDiagnostic(DiagnosticEnvelope envelope) { }
        public void WriteAudit(AuditEnvelope envelope) => Audits.Add(envelope);
        public void WriteActivity(CompletedActivityEnvelope envelope) { }
        public void Report(EvidenceGap gap) => Gaps.Add(gap);
    }
}
