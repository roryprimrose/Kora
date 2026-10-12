using System.Diagnostics;
using System.Text;

using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionQueueTests
{
    [WindowsFact]
    public async Task Mixed_profiles_keep_session_FIFO_fairness_dependencies_and_real_pause_without_automatic_resume()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var other = (await WindowsSqliteSessionWorkspaceTests.Service(f, new())
            .CreateAsync(new("Independent"), RequestOrigin.LocalUi, f.Token)).Authority.SessionId;
        var a1 = await EnqueueAsync(f, f.Request.SessionId);
        var a2 = await EnqueueAsync(f, f.Request.SessionId, profile: SessionQueueReadProfile.Capabilities);
        var b1 = await EnqueueAsync(f, other, profile: SessionQueueReadProfile.Readiness);
        var b2 = await EnqueueAsync(f, other, profile: SessionQueueReadProfile.Runtimes, dependency: a2.Request.TaskId);
        var a3 = await EnqueueAsync(f, f.Request.SessionId, profile: SessionQueueReadProfile.Capability,
            target: new(ReadOnlyCapabilityCatalog.RuntimeStatus));
        var b3 = await EnqueueAsync(f, other, profile: SessionQueueReadProfile.RuntimeStatus);
        using var actions = new FixedRegistryFixture(f.Time);
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), new WindowsSqliteSessionWorkspaceTests.Access(),
            new DeterministicVersionQueueAction(actions.Registry), new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var before = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
        var paused = await queue.ExecuteCommandAsync(PauseCommand(before, true), RequestOrigin.LocalUi, () => true, f.Token);
        var first = await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch, f.Request.SessionId.Value, 1)
            { QueueRevision = before.Revision }, RequestOrigin.LocalUi, () => true, f.Token);
        first.QueueDispatch!.Select(receipt => receipt.Entry.Request.TaskId).Should().Equal(b1.Request.TaskId);
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().Equal(a1, a2, a3);
        await queue.ExecuteCommandAsync(PauseCommand(paused.Queue!, false), RequestOrigin.LocalUi, () => true, f.Token);
        (await f.Store.ReadQueueEntryAsync(other, b2.Request.TaskId, f.Token))!.State.Should().Be(SessionQueueState.Pending);
        var next = await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch, f.Request.SessionId.Value, 1)
            { QueueRevision = before.Revision }, RequestOrigin.LocalUi, () => true, f.Token);
        next.QueueDispatch!.Select(receipt => receipt.Entry.Request.TaskId).Should().Equal(a1.Request.TaskId, a2.Request.TaskId,
            b2.Request.TaskId, a3.Request.TaskId, b3.Request.TaskId);
        next.QueueDispatch.Should().OnlyContain(receipt => receipt.Entry.State == SessionQueueState.Succeeded);
        actions.Probe.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(SessionQueueReadProfile.Version)]
    [InlineData(SessionQueueReadProfile.Capabilities)]
    [InlineData(SessionQueueReadProfile.Readiness)]
    [InlineData(SessionQueueReadProfile.Runtimes)]
    [InlineData(SessionQueueReadProfile.Capability)]
    [InlineData(SessionQueueReadProfile.RuntimeStatus)]
    public async Task All_actual_profiles_suppress_cancelled_private_retired_and_equal_deadline_data(SessionQueueReadProfile profile)
    {
        foreach (var boundary in new[] { "cancel", "private", "late", "commit-late" })
        {
            using var f = new InteractionStorageFixture();
            using var cancellation = new CancellationTokenSource();
            var armed = false;
            f.Reopen(new InteractionTransactionCheckpoint
            {
                Commit = (_, _) =>
                {
                    if (armed && string.Equals(boundary, "commit-late", StringComparison.Ordinal))
                    { f.Time.MonotonicTicks += TimeSpan.FromMinutes(5).Ticks; }
                },
            });
            await f.InitializeAsync();
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
            var pending = await EnqueueAsync(f, f.Request.SessionId, profile: profile, target: ProfileTarget(profile));
            using var actions = new FixedRegistryFixture(f.Time);
            var access = new WindowsSqliteSessionWorkspaceTests.Access();
            var action = new InterceptedFixedRead(new DeterministicVersionQueueAction(actions.Registry), () =>
            {
                if (string.Equals(boundary, "cancel", StringComparison.Ordinal)) { cancellation.Cancel(); }
                if (string.Equals(boundary, "private", StringComparison.Ordinal)) { access.ControlRevision++; }
                if (string.Equals(boundary, "late", StringComparison.Ordinal)) { f.Time.MonotonicTicks += TimeSpan.FromMinutes(5).Ticks; }
                armed = true;
            });
            await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access, action,
                new(), NullLogger<SessionQueueService>.Instance, f.Time);
            var snapshot = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
            var dispatch = () => queue.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch, f.Request.SessionId.Value, 1)
                { QueueRevision = snapshot.Revision }, RequestOrigin.LocalUi, () => true, cancellation.Token);
            if (string.Equals(boundary, "late", StringComparison.Ordinal))
            {
                var receipt = (await dispatch()).QueueDispatch!.Single();
                receipt.Entry.State.Should().Be(SessionQueueState.Failed);
                receipt.Result.Should().BeNull();
                receipt.Version.Should().BeNull();
            }
            else { await dispatch.Should().ThrowAsync<Exception>(); }
            armed = false;
            var entry = (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, pending.Request.TaskId, f.Token))!;
            entry.State.Should().NotBe(SessionQueueState.Succeeded);
            entry.Result.Should().BeNull();
            actions.Probe.Calls.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(SessionQueueReadProfile.Version, RequestOrigin.LocalUi, null)]
    [InlineData(SessionQueueReadProfile.Capabilities, RequestOrigin.LocalUi, null)]
    [InlineData(SessionQueueReadProfile.Readiness, RequestOrigin.ActivatedVoice, null)]
    [InlineData(SessionQueueReadProfile.Runtimes, RequestOrigin.ActivatedVoice, null)]
    [InlineData(SessionQueueReadProfile.RuntimeStatus, RequestOrigin.ActivatedVoice, null)]
    [InlineData(SessionQueueReadProfile.Capability, RequestOrigin.LocalUi, ReadOnlyCapabilityCatalog.List)]
    [InlineData(SessionQueueReadProfile.Capability, RequestOrigin.ActivatedVoice, ReadOnlyCapabilityCatalog.Get)]
    [InlineData(SessionQueueReadProfile.Capability, RequestOrigin.LocalUi, ReadOnlyCapabilityCatalog.Version)]
    [InlineData(SessionQueueReadProfile.Capability, RequestOrigin.ActivatedVoice, ReadOnlyCapabilityCatalog.Readiness)]
    [InlineData(SessionQueueReadProfile.Capability, RequestOrigin.LocalUi, ReadOnlyCapabilityCatalog.RuntimeList)]
    [InlineData(SessionQueueReadProfile.Capability, RequestOrigin.ActivatedVoice, ReadOnlyCapabilityCatalog.RuntimeStatus)]
    public async Task Actual_registry_dispatch_commits_bounded_typed_results_and_exact_private_status_without_probes(
        SessionQueueReadProfile profile, RequestOrigin origin, string? targetId)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        using var actions = new FixedRegistryFixture(f.Time);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), access,
            new DeterministicVersionQueueAction(actions.Registry), new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var workspace = new SessionWorkspaceService(f.Store, new(f.Tasks), access, NullLogger<SessionWorkspaceService>.Instance, queue);
        var command = new SessionCommand(SessionCommandOperation.QueueEnqueue, f.Request.SessionId.Value, 1)
        { QueueProfile = profile, QueueTarget = targetId is null ? null : new(targetId), WorkRequestId = Guid.NewGuid(), TaskId = Guid.NewGuid() };
        var enqueued = await workspace.ExecuteCommandAsync(command, origin, () => true, f.Token);
        var pending = enqueued.Queue!.Entries.Single();
        var pendingPayload = QueuePayload(f, pending);
        var originalAudits = AuditPayloads(f);
        actions.Probe.Calls.Should().Be(0);
        actions.Dependencies.RecordObservation(new("local.inference", "recorded", DependencyReadiness.Ready, "private detail must not appear"));
        var recorded = actions.Dependencies.Observations;
        var dispatched = await workspace.ExecuteCommandAsync(new(SessionCommandOperation.QueueDispatch, f.Request.SessionId.Value, 1)
            { QueueRevision = enqueued.Queue.Revision }, origin, () => true, f.Token);
        var receipt = dispatched.QueueDispatch!.Single();
        receipt.Entry.ReadProfile.Should().Be(profile);
        receipt.Entry.State.Should().Be(SessionQueueState.Succeeded);
        receipt.Entry.ExpiresAt.Should().Be(pending.ExpiresAt);
        receipt.Entry.ActiveDeadlineAt.Should().Be(receipt.Entry.AdmittedAt!.Value.AddMinutes(5));
        receipt.Entry.Result.Should().BeNull();
        actions.Probe.Calls.Should().Be(0);
        actions.Dependencies.Observations.Should().BeSameAs(recorded);
        var status = await workspace.ExecuteCommandAsync(new(SessionCommandOperation.QueueStatus, f.Request.SessionId.Value)
            { TaskId = command.TaskId }, origin, () => true, f.Token);
        var stored = status.QueueEntry!;
        if (profile == SessionQueueReadProfile.Version)
        {
            receipt.Version!.Version.Should().Be("4.5.6");
            stored.RecordVersion.Should().Be(3);
            stored.Result.Should().BeNull();
        }
        else
        {
            stored.RecordVersion.Should().Be(profile is SessionQueueReadProfile.Capability or SessionQueueReadProfile.RuntimeStatus ? 5 : 4);
            CapabilityReplyWire.Serialize(stored.Result!).Should().Equal(CapabilityReplyWire.Serialize(receipt.Result!));
            CapabilityReplyWire.Serialize(stored.Result!).Length.Should().BeLessThanOrEqualTo(4096);
            Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(stored.Result!)).Should().NotContain("private detail");
            SessionQueueReadProfiles.Accepts(profile, stored.Result!, command.QueueTarget).Should().BeTrue();
        }
        stored.Target.Should().Be(command.QueueTarget);
        if (targetId is not null) { stored.Result!.Descriptor!.Id.Should().Be(targetId); }
        if (profile == SessionQueueReadProfile.RuntimeStatus)
        {
            stored.Result!.Runtime!.Id.Should().Be(ReadOnlyCapabilityCatalog.LocalRuntimeId);
            stored.Result.Runtime.ObservedAt.Should().Be(recorded.Single().ObservedAt);
            stored.Result.Runtime.ToolLoopQualified.Should().BeFalse();
        }
        if (profile == SessionQueueReadProfile.Capabilities) { receipt.Result!.Capabilities!.Records.Should().HaveCount(6); }
        if (profile == SessionQueueReadProfile.Readiness)
        {
            receipt.Result!.Readiness!.Records.Should().HaveCount(6);
            receipt.Result.Readiness.Records.Single(row => string.Equals(row.Id, "local.inference", StringComparison.Ordinal)).ObservedAt.Should().Be(recorded.Single().ObservedAt);
        }
        if (profile == SessionQueueReadProfile.Runtimes)
        {
            receipt.Result!.Runtimes!.Records.Should().ContainSingle().Which.ToolLoopQualified.Should().BeFalse();
        }
        var work = await f.Store.ReadWorkAsync(f.Request.SessionId, 1, new(), f.Token);
        work.QueueRecords.Single().Entry.Result.Should().BeNull();
        using (var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            var all = await workspace.ReadWorkOverviewAsync(SessionListFilter.All, false, null, f.Token);
            all.Records.Single().Work!.QueueRecords.Single().Entry.Result.Should().BeNull();
        }
        var history = await f.Store.ReadHistoryAsync(f.Request.SessionId, null, 50, f.Token);
        history.Records.Where(row => row.TaskId == command.TaskId).Should().HaveCount(3);
        AuditPayloads(f).Take(originalAudits.Length).Should().Equal(originalAudits);
        RequireQueueDigest(f, stored, QueuePayload(f, stored));
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var reopened = (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, pending.Request.TaskId, f.Token))!;
        QueuePayload(f, reopened).Should().Be(QueuePayload(f, stored));
        (await f.Store.FindReadyAsync(1, new(), f.Token)).Should().BeNull();
        pendingPayload.Should().NotContain("Result");
    }

    [Theory]
    [InlineData("json_remove(payload,'$.Profile')")]
    [InlineData("json_set(payload,'$.Profile',99)")]
    [InlineData("json_set(payload,'$.Profile','capabilities.get')")]
    [InlineData("json_set(payload,'$.RecordVersion',2)")]
    [InlineData("json_set(payload,'$.Arguments','{}')")]
    [InlineData("json_set(payload,'$.Count',1)")]
    [InlineData("json_set(payload,'$.Offset',1)")]
    [InlineData("json_set(payload,'$.Profile',2)")]
    [InlineData("json_remove(payload,'$.Result')")]
    [InlineData("json_set(payload,'$.Result.Outcome',1)")]
    [InlineData("json_set(payload,'$.Result.Capabilities.NextOffset',1)")]
    public async Task Invalid_or_audit_mismatched_profile_argument_page_and_result_persistence_refuses_without_replacement(string mutation)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var pending = await EnqueueAsync(f, f.Request.SessionId, profile: SessionQueueReadProfile.Capabilities);
        var admitted = await AdmitAsync(f, pending);
        using (var root = HostActivity.BeginRoot(admitted.Request, HostActivityLayer.Application, HostOperation.Tool))
        {
            await f.Store.CompleteAsync(admitted, SessionQueueState.Succeeded, () => true, f.Token,
                new(CapabilityOutcome.Succeeded, "catalogue", Capabilities: new(ReadOnlyCapabilityCatalog.Descriptors, 6, null)));
        }
        f.Mutate("UPDATE session_queue SET payload=" + mutation + ";");
        f.Reopen();
        var initialize = () => f.Store.InitializeAsync(f.Token).AsTask();
        await initialize.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(SessionQueueReadProfile.Version)]
    [InlineData(SessionQueueReadProfile.Capabilities)]
    [InlineData(SessionQueueReadProfile.Readiness)]
    [InlineData(SessionQueueReadProfile.Runtimes)]
    [InlineData(SessionQueueReadProfile.Capability)]
    [InlineData(SessionQueueReadProfile.RuntimeStatus)]
    public async Task Restart_pending_and_dispatched_profiles_project_interrupted_unknown_without_replay(SessionQueueReadProfile profile)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(f, HostTaskState.Succeeded);
        var first = await EnqueueAsync(f, f.Request.SessionId, profile: profile, target: ProfileTarget(profile));
        var later = await EnqueueAsync(f, f.Request.SessionId, profile: profile, target: ProfileTarget(profile));
        var running = await AdmitAsync(f, first);
        var payload = QueuePayload(f, running);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, first.Request.TaskId, f.Token))!.State.Should().Be(SessionQueueState.Unknown);
        (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, later.Request.TaskId, f.Token))!.State.Should().Be(SessionQueueState.Interrupted);
        QueuePayload(f, running).Should().Be(payload);
        (await f.Store.FindReadyAsync(1, new(executionSlots: 2), f.Token)).Should().BeNull();
    }

    internal sealed class FixedRegistryFixture : IDisposable
    {
        internal NeverProbe Probe { get; } = new();
        internal DependencyBootstrapper Dependencies { get; }
        internal ReadOnlyCapabilityRegistry Registry { get; }
        internal FixedRegistryFixture(TimeProvider time)
        {
            Dependencies = new([Probe], NullLogger<DependencyBootstrapper>.Instance, time);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(Dependencies);
            Registry = new(new FixedHost(), new Kora.Tools.Capabilities.CapabilitiesList(), new Kora.Tools.Capabilities.CapabilitiesGet(),
                new Kora.Tools.Application.ApplicationGetVersion(new FixedInfo()), new Kora.Tools.Readiness.ReadinessGet(Dependencies),
                new Kora.Tools.Runtime.RuntimeList(runtime), new Kora.Tools.Runtime.RuntimeGetStatus(runtime),
                NullLogger<ReadOnlyCapabilityRegistry>.Instance);
        }
        public void Dispose() => Dependencies.Dispose();
    }

    internal sealed class NeverProbe : IDependencyProbe
    {
        internal int Calls { get; private set; }
        public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("Queued reads must not probe.");
        }
    }
    private sealed class InterceptedFixedRead(IDeterministicVersionQueueAction action, Action afterRead) : IDeterministicVersionQueueAction
    {
        public CapabilityReply Observe(SessionQueueReadProfile profile, CancellationToken token, SessionQueueCapabilityTarget? target = null)
        {
            var reply = action.Observe(profile, token, target);
            afterRead();
            return reply;
        }
    }
    private sealed class FixedHost : ICapabilityHostAccess { public bool IsCurrentHost => true; }
    private sealed class FixedInfo : IApplicationInfo { public string Version => "4.5.6"; }
    private static SessionQueueCapabilityTarget? ProfileTarget(SessionQueueReadProfile profile) =>
        profile == SessionQueueReadProfile.Capability ? new(ReadOnlyCapabilityCatalog.Version) : null;
}
