using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task Retention_observations_are_passive_and_exact_controls_commit_fresh_intent_terminal_and_readback()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var before = await fixture.Service.ReadRetentionAsync(fixture.Session.SessionId, fixture.Token);
        fixture.TaskWrites.Should().BeEmpty();
        var after = await fixture.Service.SetRetentionHoldAsync(before, true, RequestOrigin.LocalUi,
            () => true, fixture.Token);
        after.State.Should().Be(before.State with { Perpetual = true });
        fixture.TaskWrites.Should().HaveCount(2);
        fixture.TaskWrites[0].Request.SessionId.Should().Be(before.State.SessionId);
        fixture.TaskWrites[1].State.Should().Be(HostTaskState.Succeeded);
        after.ExemptionAuditSequence.Should().Be(1);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData((RequestOrigin)99)]
    public async Task Retention_control_rejects_non_native_origins(RequestOrigin origin)
    {
        using var fixture = new Fixture();
        var action = () => fixture.Service.SetRetentionHoldAsync(fixture.Retention, true, origin, () => true, fixture.Token);
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Retention_control_rejects_system_or_stopped_ambient_context(bool native, bool stopped)
    {
        using var fixture = new Fixture();
        var request = new HostRequest(new(Guid.NewGuid()), fixture.Session.SessionId, new(Guid.NewGuid()),
            native ? RequestOrigin.LocalUi : RequestOrigin.HostSystem);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (stopped) { root.Activity!.Stop(); }
        if (native && !stopped)
        {
            await fixture.Service.SetRetentionHoldAsync(fixture.Retention, true, RequestOrigin.LocalUi, () => true, fixture.Token);
        }

        else
        {
            var action = () => fixture.Service.SetRetentionHoldAsync(fixture.Retention, true, RequestOrigin.LocalUi, () => true, fixture.Token);
            await action.Should().ThrowAsync<InvalidOperationException>();
            fixture.TaskWrites.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Retention_status_rejects_foreign_subjects_and_purged_controls()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var read = () => fixture.Service.ReadRetentionAsync(new(Guid.NewGuid()), fixture.Token);
        await read.Should().ThrowAsync<InvalidDataException>();
        var purged = fixture.Retention with { State = fixture.Retention.State with { Purged = true } };
        var set = () => fixture.Service.SetRetentionHoldAsync(purged, false, RequestOrigin.LocalUi, () => true, fixture.Token);
        await set.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Retention_removed_metadata_observation_never_becomes_control_authority()
    {
        using var fixture = new Fixture();
        var action = () => fixture.Service.SetRetentionHoldAsync(fixture.Retention with { Removed = true },
            true, RequestOrigin.LocalUi, () => true, fixture.Token);
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("model")]
    [InlineData("restored-stopped")]
    public async Task Retention_control_rejects_foreign_invocation_and_replaced_stopped_request_context(string context)
    {
        using var fixture = new Fixture();
        var request = new HostRequest(new(Guid.NewGuid()),
            context is "foreign" ? new(Guid.NewGuid()) : fixture.Session.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi, context is "model" ? new(Guid.NewGuid()) : null);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (context is "restored-stopped")
        {
            root.Activity!.Stop();
            System.Diagnostics.Activity.Current = root.Activity;
        }
        var action = () => fixture.Service.SetRetentionHoldAsync(fixture.Retention, true, RequestOrigin.LocalUi, () => true, fixture.Token);
        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Retention_control_rejects_missing_expected_state_and_admission()
    {
        using var fixture = new Fixture();
        var missingState = () => fixture.Service.SetRetentionHoldAsync(null!, true, RequestOrigin.LocalUi, () => true, fixture.Token);
        await missingState.Should().ThrowAsync<ArgumentNullException>();
        var missingAdmission = () => fixture.Service.SetRetentionHoldAsync(fixture.Retention, true, RequestOrigin.LocalUi, null!, fixture.Token);
        await missingAdmission.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Retention_cancellation_has_no_success_receipt_or_false_rollback()
    {
        using var fixture = new Fixture { CancelRetentionControl = true };
        var action = () => fixture.Service.SetRetentionHoldAsync(fixture.Retention, true, RequestOrigin.LocalUi, () => true, fixture.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.TaskWrites.Should().NotContain(record => record.State == HostTaskState.Succeeded);
    }

    [Theory]
    [InlineData("admission")]
    [InlineData("state")]
    [InlineData("generation")]
    [InlineData("audit")]
    public async Task Committed_retention_with_failed_readback_is_not_reported_as_rollback_or_success(string failure)
    {
        using var fixture = new Fixture { RetentionReadbackFailure = failure };
        var set = () => fixture.Service.SetRetentionHoldAsync(fixture.Retention, true, RequestOrigin.LocalUi,
            () => fixture.CanControl, fixture.Token);
        await set.Should().ThrowAsync<InvalidOperationException>();
        fixture.Retention.State.Perpetual.Should().BeTrue("readback uncertainty is not rollback");
        fixture.TaskWrites.Last().State.Should().Be(HostTaskState.Succeeded);
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("status")]
    public async Task Bound_passive_inspection_checks_current_policy_and_source_revision_without_cleanup(string scenario)
    {
        using var fixture = new Fixture();
        var controlStore = new AudioControlTestStore();
        await using var admission = new DiagnosticRetentionAdmission(controlStore, controlStore, new(controlStore));
        var configuration = new SessionRetentionConfigurationService(new RetentionPreferences(),
            new(), admission, new RetentionAudit());
        configuration.Observe();
        var retentionStore = new RetentionStore();
        await using var retention = new SessionRetentionService(retentionStore, configuration, fixture,
            new RetentionDispatcher(), TimeProvider.System, NullLogger<SessionRetentionService>.Instance);
        fixture.Service.BindRetention(retention);
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        fixture.AfterListRead = () =>
        {
            if (scenario is "source") { fixture.PassiveRevision++; }
            if (scenario is "privacy") { fixture.CanControl = false; }
        };
        if (scenario is "status")
        {
            await fixture.Service.ReadRetentionAsync(fixture.Session.SessionId, fixture.Token);
            fixture.PassiveInspectionCalls.Should().Be(0);
        }
        else if (scenario is "unchanged")
        {
            await fixture.Service.ReadMetadataAsync(null, 25, fixture.Token);
            fixture.PassiveInspectionCalls.Should().Be(2);
        }
        else
        {
            var read = () => fixture.Service.ReadMetadataAsync(null, 25, fixture.Token);
            await read.Should().ThrowAsync<InvalidOperationException>();
        }
        retentionStore.CleanupCalls.Should().Be(0);
    }

    private sealed class RetentionPreferences : ISessionRetentionPreferences
    {
        public SessionRetentionSettings? Load() => null;
        public SessionRetentionSettings? ReadBack() => null;
        public void BeginWrite() => throw new NotSupportedException();
        public void ConfirmWrite() => throw new NotSupportedException();
        public void Save(SessionRetentionSettings settings) => throw new NotSupportedException();
        public void Reset() => throw new NotSupportedException();
    }

    private sealed class RetentionAudit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) => throw new NotSupportedException();
    }

    private sealed class RetentionDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
        public Task InvokeAsync(Func<Task> action) => action();
    }

    private sealed class RetentionStore : ISessionRetentionStore
    {
        internal int CleanupCalls { get; private set; }
        public ValueTask<SessionRetentionState> ReadRetentionAsync(HostId<SessionIdentity> session, CancellationToken token) =>
            throw new NotSupportedException();
        public ValueTask SetPerpetualAsync(HostRequest request, HostRevision generation, bool perpetual,
            Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionRetentionBatch> ApplyRetentionAsync(Func<bool> admitted,
            Func<HostId<SessionIdentity>, CancellationToken, ValueTask> revokeSources, CancellationToken token)
        {
            CleanupCalls++;
            throw new NotSupportedException();
        }
    }

    private sealed partial class Fixture
    {
        private SessionRetentionObservation? savedRetention;
        internal long PassiveRevision { get; set; }
        internal int PassiveInspectionCalls { get; private set; }
        internal string? RetentionReadbackFailure { get; init; }
        internal bool CancelRetentionControl { get; init; }
        internal SessionRetentionObservation Retention => savedRetention ?? new(
            new(Session.SessionId, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1),
                DateTimeOffset.UnixEpoch.AddDays(30), false, false), Session.Generation, 0, false)
                { ObservedAt = DateTimeOffset.UnixEpoch };
        public ValueTask<long> ValidatePassiveInspectionAsync(CancellationToken token)
        {
            PassiveInspectionCalls++;
            return ValueTask.FromResult(PassiveRevision);
        }
        public ValueTask<SessionRetentionObservation> ReadRetentionObservationAsync(HostId<SessionIdentity> session, CancellationToken token)
        {
            var value = Retention;
            if (savedRetention is not null)
            {
                if (RetentionReadbackFailure is "admission") { CanControl = false; }
                value = RetentionReadbackFailure switch
                {
                    "state" => value with { State = value.State with { Perpetual = false } },
                    "generation" => value with { Generation = new(2) },
                    "audit" => value with { ExemptionAuditSequence = 99 },
                    _ => value,
                };
            }
            return ValueTask.FromResult(value);
        }
        public ValueTask<SessionRetentionObservation> SetRetentionHoldAsync(HostRequest request,
            SessionRetentionObservation expected, bool perpetual, Func<bool> admitted, CancellationToken token)
        {
            if (CancelRetentionControl) { throw new OperationCanceledException("Retention commit cancelled."); }
            if (!admitted()) { throw new InvalidOperationException("Admission changed."); }
            savedRetention = expected with
            {
                State = expected.State with { Perpetual = perpetual }, ExemptionAuditSequence = 1,
            };
            return ValueTask.FromResult(savedRetention);
        }
    }
}
