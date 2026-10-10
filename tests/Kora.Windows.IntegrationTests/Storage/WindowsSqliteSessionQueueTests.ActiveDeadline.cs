using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionQueueTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAtReceiptAuditOrCommitCannotCommitLateSuccessOrFreeSession(bool atCommit)
    {
        using var f = new InteractionStorageFixture();
        using var cancellation = new CancellationTokenSource();
        var armed = false;
        f.Reopen(new InteractionTransactionCheckpoint
        {
            Audit = (_, _) => { if (armed && !atCommit) { cancellation.Cancel(); } },
            Commit = (_, _) => { if (armed && atCommit) { cancellation.Cancel(); } },
        });
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var pending = await EnqueueAsync(f, f.Request.SessionId);
        var action = new DeadlineAction(() => armed = true);
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks),
            new WindowsSqliteSessionWorkspaceTests.Access(), action, new(),
            NullLogger<SessionQueueService>.Instance, f.Time);
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var dispatch = () => queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch, f.Request.SessionId.Value, 1)
            { QueueRevision = snapshot.Revision }, RequestOrigin.LocalUi, () => true, cancellation.Token);
        await dispatch.Should().ThrowAsync<InvalidOperationException>();
        armed = false;
        var entry = (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, pending.Request.TaskId, f.Token))!;
        entry.State.Should().Be(SessionQueueState.Running);
        entry.ActiveBudgetMinutes.Should().Be(5);
        (await f.Store.FindReadyAsync(1, new(executionSlots: 2), f.Token)).Should().BeNull();
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, pending.Request.TaskId, f.Token))!.State.Should().Be(SessionQueueState.Unknown);
    }

    [Theory]
    [InlineData(1, -1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(5, -1)]
    [InlineData(5, 0)]
    [InlineData(5, 1)]
    [InlineData(60, -1)]
    [InlineData(60, 0)]
    [InlineData(60, 1)]
    public async Task ActualDispatchMonotonicBoundaryMatchesPreciseCommittedWorkDeadlineAndSuppressesLateVersion(int minutes, int edge)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var pending = await EnqueueAsync(f, f.Request.SessionId, minutes: 1);
        var oldPayload = QueuePayload(f, pending);
        var priorAudits = AuditPayloads(f);
        f.Time.Now = f.Time.Now.AddSeconds(7).AddTicks(123);
        var admissionTime = f.Time.Now;
        f.Time.MonotonicTicks = TimeSpan.FromMinutes(40).Ticks;
        var action = new DeadlineAction(() =>
        {
            f.Time.MonotonicTicks += TimeSpan.FromMinutes(minutes).Ticks + edge;
            f.Time.Now = f.Time.Now.AddSeconds(1);
        });
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks),
            new WindowsSqliteSessionWorkspaceTests.Access(), action, new(activeBudgetMinutes: minutes),
            NullLogger<SessionQueueService>.Instance, f.Time);
        var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var result = await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch, f.Request.SessionId.Value, 1)
            { QueueRevision = snapshot.Revision }, RequestOrigin.LocalUi, () => true, f.Token);
        var receipt = result.QueueDispatch!.Single();
        receipt.Entry.ActiveBudgetMinutes.Should().Be(minutes);
        receipt.Entry.AdmittedAt.Should().Be(admissionTime);
        receipt.Entry.ActiveDeadlineAt.Should().Be(admissionTime.AddMinutes(minutes));
        receipt.Entry.EnqueuedAt.Should().Be(pending.EnqueuedAt);
        receipt.Entry.ExpiresAt.Should().Be(pending.ExpiresAt);
        receipt.Entry.State.Should().Be(edge < 0 ? SessionQueueState.Succeeded : SessionQueueState.Failed);
        if (edge < 0) { receipt.Version!.Version.Should().Be("7.8.9"); }
        else { receipt.Version.Should().BeNull(); }
        var work = await f.Store.ReadWorkAsync(f.Request.SessionId, 1, new(activeBudgetMinutes: minutes == 1 ? 60 : 1), f.Token);
        work.QueueRecords.Single().ActiveDeadline.Should().Be(admissionTime.AddMinutes(minutes));
        using var output = System.Text.Json.JsonDocument.Parse(SessionCommandResult.Serialize(new("observed", SessionWorkSnapshot.Scope) { Work = work }));
        output.RootElement.GetProperty("work").GetProperty("queueRecords")[0].GetProperty("activeDeadline")
            .GetDateTimeOffset().Should().Be(admissionTime.AddMinutes(minutes));
        AuditPayloads(f).Take(priorAudits.Length).Should().Equal(priorAudits);
        oldPayload.Should().Contain("\"RecordVersion\":2").And.NotContain("ActiveBudgetMinutes");
        RequireQueueDigest(f, receipt.Entry, QueuePayload(f, receipt.Entry));
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var reopened = await f.Store.ReadWorkAsync(f.Request.SessionId, 1, new(activeBudgetMinutes: 5), f.Token);
        reopened.QueueRecords.Single().ActiveDeadline.Should().Be(admissionTime.AddMinutes(minutes));
        reopened.QueueRecords.Single().Entry.Should().Be(receipt.Entry);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(60)]
    public async Task RestartUnknownRetainsOriginalActiveAuthorityWithoutReplayOrFreeingSession(int minutes)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var pending = await EnqueueAsync(f, f.Request.SessionId);
        f.Time.Now = f.Time.Now.AddTicks(317);
        var running = await AdmitAsync(f, pending, budget: minutes);
        var payload = QueuePayload(f, running);
        var audits = AuditPayloads(f);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var work = await f.Store.ReadWorkAsync(f.Request.SessionId, 1, new(activeBudgetMinutes: minutes == 1 ? 60 : 1), f.Token);
        var row = work.QueueRecords.Single();
        row.Entry.State.Should().Be(SessionQueueState.Unknown);
        row.ActiveDeadline.Should().Be(running.ActiveDeadlineAt);
        row.Entry.ActiveBudgetMinutes.Should().Be(minutes);
        row.Eligibility.Should().Be(SessionQueueEligibility.UnknownQuarantine);
        QueuePayload(f, running).Should().Be(payload);
        AuditPayloads(f).Take(audits.Length).Should().Equal(audits);
        (await f.Store.FindReadyAsync(1, new(executionSlots: 2), f.Token)).Should().BeNull();
        AuthorityLocalEventSource.FromWork(work, f.Time.Now).Single().Type.Should().Be(Kora.Core.Interaction.LocalEventType.Unknown);
    }

    [Theory]
    [InlineData("json_set(payload,'$.RecordVersion',2)")]
    [InlineData("json_remove(payload,'$.ActiveBudgetMinutes')")]
    [InlineData("json_remove(payload,'$.ActiveDeadlineAt')")]
    [InlineData("json_remove(payload,'$.AdmittedAt')")]
    [InlineData("json_set(payload,'$.ActiveBudgetMinutes',0)")]
    [InlineData("json_set(payload,'$.ActiveBudgetMinutes',61)")]
    [InlineData("json_set(payload,'$.RecordVersion',4)")]
    [InlineData("json_set(payload,'$.ActiveBudgetMinutes',60)")]
    [InlineData("json_remove(payload,'$.RecordVersion','$.PendingLifetimeMinutes','$.ActiveBudgetMinutes','$.ActiveDeadlineAt','$.AdmittedAt')")]
    public async Task CorruptPartialDowngradedAndUnboundActivePayloadCannotReopenOrRewriteAuthority(string expression)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var pending = await EnqueueAsync(f, f.Request.SessionId);
        var running = await AdmitAsync(f, pending);
        f.Mutate("UPDATE session_queue SET payload=" + expression + ";");
        var payload = QueuePayload(f, running);
        var audits = AuditPayloads(f);
        f.Reopen();
        var reopen = () => f.Store.InitializeAsync(f.Token).AsTask();
        await reopen.Should().ThrowAsync<InvalidDataException>();
        QueuePayload(f, running).Should().Be(payload);
        AuditPayloads(f).Should().Equal(audits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanonicalTerminalPayloadAndLatestDigestCannotRebindOriginalAdmission(bool timeChanged)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var pending = await EnqueueAsync(f, f.Request.SessionId);
        var admitted = await AdmitAsync(f, pending);
        var terminal = await CompleteAsync(f, admitted);
        var altered = timeChanged ? terminal with { AdmittedAt = terminal.AdmittedAt!.Value.AddTicks(1),
            ActiveDeadlineAt = terminal.ActiveDeadlineAt!.Value.AddTicks(1) }
            : terminal with { ActiveBudgetMinutes = 60, ActiveDeadlineAt = terminal.AdmittedAt!.Value.AddMinutes(60) };
        altered.Validate();
        var payload = HostInteractionCodec.Encode(altered);
        StageKnownLegacyQueue(f, altered, payload, latestOnly: true);
        var audits = AuditPayloads(f);
        f.Reopen();
        var reopen = () => f.Store.InitializeAsync(f.Token).AsTask();
        await reopen.Should().ThrowAsync<InvalidDataException>();
        QueuePayload(f, altered).Should().Be(payload);
        AuditPayloads(f).Should().Equal(audits);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("commit")]
    [InlineData("cancel")]
    [InlineData("eligibility")]
    public async Task ActiveAdmissionFailureRollsBackBudgetTaskSlotAndRequiredAudit(string stage)
    {
        using var f = new InteractionStorageFixture();
        using var cancellation = new CancellationTokenSource();
        var armed = false;
        var eligible = true;
        var checkpoint = new InteractionTransactionCheckpoint
        {
            Audit = (_, _) => { if (armed && stage is "audit") { throw new IOException("Missing audit"); } },
            Commit = (_, _) =>
            {
                if (!armed) { return; }
                if (stage is "commit") { throw new IOException("Interrupted commit"); }
                if (stage is "cancel") { cancellation.Cancel(); }
                if (stage is "eligibility") { eligible = false; }
            },
        };
        f.Reopen(checkpoint);
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var pending = await EnqueueAsync(f, f.Request.SessionId);
        var payload = QueuePayload(f, pending);
        var audits = AuditPayloads(f);
        armed = true;
        using var root = HostActivity.BeginRoot(pending.Request, HostActivityLayer.Application, HostOperation.Request);
        var admit = () => f.Store.AdmitAsync(pending, 1, new(activeBudgetMinutes: 60), () => eligible, cancellation.Token).AsTask();
        await admit.Should().ThrowAsync<Exception>();
        QueuePayload(f, pending).Should().Be(payload);
        AuditPayloads(f).Should().Equal(audits);
        armed = false;
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, pending.Request.TaskId, f.Token)).Should().Be(pending);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().Be(pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldPendingFormatUsesFutureAdmissionBudgetWithoutRewritingEnqueueAuthority(bool lifetimeFormat)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var created = await EnqueueAsync(f, f.Request.SessionId);
        var pending = created with { RecordVersion = lifetimeFormat ? 2 : null, PendingLifetimeMinutes = lifetimeFormat ? 30 : null };
        var payload = HostInteractionCodec.Encode(pending);
        StageKnownLegacyQueue(f, pending, payload);
        var audits = AuditPayloads(f);
        QueuePayload(f, pending).Should().Be(payload);
        var admitted = await AdmitAsync(f, pending, budget: 60);
        admitted.ActiveBudgetMinutes.Should().Be(60);
        admitted.ActiveDeadlineAt.Should().Be(f.Time.Now.AddMinutes(60));
        admitted.EnqueuedAt.Should().Be(pending.EnqueuedAt);
        admitted.ExpiresAt.Should().Be(pending.ExpiresAt);
        AuditPayloads(f).Take(audits.Length).Should().Equal(audits);
        await CompleteAsync(f, admitted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownLegacyAdmittedFormatsKeepFiveMinuteDeadlineAndExactBytesOnReopen(bool lifetimeFormat)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var pending = await EnqueueAsync(f, f.Request.SessionId);
        var running = await AdmitAsync(f, pending);
        var legacy = running with { RecordVersion = lifetimeFormat ? 2 : null,
            PendingLifetimeMinutes = lifetimeFormat ? 30 : null, ActiveBudgetMinutes = null, AdmittedAt = null, ActiveDeadlineAt = null };
        var payload = HostInteractionCodec.Encode(legacy);
        StageKnownLegacyQueue(f, legacy, payload);
        var audits = AuditPayloads(f);
        var work = await f.Store.ReadWorkAsync(f.Request.SessionId, 1, new(activeBudgetMinutes: 60), f.Token);
        work.QueueRecords.Single().ActiveDeadline.Should().Be(f.Time.Now.AddMinutes(5));
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var reopened = await f.Store.ReadWorkAsync(f.Request.SessionId, 1, new(activeBudgetMinutes: 1), f.Token);
        reopened.QueueRecords.Single().ActiveDeadline.Should().Be(f.Time.Now.AddMinutes(5));
        reopened.QueueRecords.Single().Entry.State.Should().Be(SessionQueueState.Unknown);
        QueuePayload(f, legacy).Should().Be(payload);
        AuditPayloads(f).Take(audits.Length).Should().Equal(audits);
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
    }

    private sealed class DeadlineAction(Action observation) : IDeterministicVersionQueueAction
    {
        public CapabilityReply Observe(CancellationToken token)
        {
            observation();
            return new(CapabilityOutcome.Succeeded, "Observed", Version: new("7.8.9", "Fixed local read."));
        }
    }
}
