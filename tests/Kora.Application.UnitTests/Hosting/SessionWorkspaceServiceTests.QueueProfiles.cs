using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(SessionQueueReadProfile.Version)]
    [InlineData(SessionQueueReadProfile.Capabilities)]
    [InlineData(SessionQueueReadProfile.Readiness)]
    [InlineData(SessionQueueReadProfile.Runtimes)]
    [InlineData(SessionQueueReadProfile.Capability)]
    [InlineData(SessionQueueReadProfile.RuntimeStatus)]
    public async Task NativeOptionalGuardForwardsExactDependencyAndRealRevisionThroughOriginalCommandService(SessionQueueReadProfile profile)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var dependency = new HostId<TaskIdentity>(Guid.NewGuid());
        await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue) with
        {
            QueueProfile = profile, QueueTarget = FixedTarget(profile), DependencyTaskId = dependency.Value, ExpectedDependencyRevision = new(7),
        }, RequestOrigin.LocalUi, () => true, f.Token);
        f.QueueRows.Single().Dependency.Should().Be(dependency);
        f.QueueRows.Single().ReadProfile.Should().Be(profile);
        f.LastExpectedDependencyRevision.Should().Be(new HostRevision(7));
        f.QueueActions.Should().Be(0);
    }

    [Theory]
    [InlineData(SessionQueueReadProfile.Version)]
    [InlineData(SessionQueueReadProfile.Capabilities)]
    [InlineData(SessionQueueReadProfile.Readiness)]
    [InlineData(SessionQueueReadProfile.Runtimes)]
    [InlineData(SessionQueueReadProfile.Capability)]
    [InlineData(SessionQueueReadProfile.RuntimeStatus)]
    public async Task Every_fixed_profile_preserves_original_route_and_truthful_result(SessionQueueReadProfile profile)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        f.QueueReply = FixedReply(profile);
        await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue) with { QueueProfile = profile, QueueTarget = FixedTarget(profile) },
            RequestOrigin.ActivatedVoice, () => true, f.Token);
        f.QueueRows.Single().ReadProfile.Should().Be(profile);
        f.QueueActions.Should().Be(0);
        var reply = await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueDispatch) with { QueueRevision = 1 },
            RequestOrigin.LocalUi, () => true, f.Token);
        var receipt = reply.QueueDispatch!.Single();
        receipt.Entry.State.Should().Be(SessionQueueState.Succeeded);
        receipt.Entry.Result.Should().BeNull("batch output does not duplicate retained data in the metadata entry");
        if (profile == SessionQueueReadProfile.Version) { receipt.Version.Should().Be(f.QueueReply.Version); receipt.Result.Should().BeNull(); }
        else { receipt.Version.Should().BeNull(); receipt.Result.Should().Be(f.QueueReply); f.QueueRows.Single().Result.Should().Be(f.QueueReply); }
        f.ActionRequests.Single().Should().Be(f.QueueRows.Single().Request);
        f.LastObservedTarget.Should().Be(FixedTarget(profile));
    }

    [Theory]
    [InlineData(SessionQueueReadProfile.Version)]
    [InlineData(SessionQueueReadProfile.Capabilities)]
    [InlineData(SessionQueueReadProfile.Readiness)]
    [InlineData(SessionQueueReadProfile.Runtimes)]
    [InlineData(SessionQueueReadProfile.Capability)]
    [InlineData(SessionQueueReadProfile.RuntimeStatus)]
    public async Task Denial_failure_wrong_shape_oversize_and_equal_deadline_never_publish_success(SessionQueueReadProfile profile)
    {
        foreach (var failure in new[] { "denied", "failed", "wrong-shape", "oversize", "late" })
        {
            using var f = new Fixture();
            await using var queue = f.QueueService();
            f.QueueReply = failure switch
            {
                "denied" => FixedReply(profile) with { Outcome = CapabilityOutcome.Denied },
                "failed" => FixedReply(profile) with { Outcome = CapabilityOutcome.Failed },
                "wrong-shape" => FixedReply(profile == SessionQueueReadProfile.Version ? SessionQueueReadProfile.Readiness : SessionQueueReadProfile.Version),
                "oversize" => FixedReply(profile) with { Reason = new string('x', 4096) },
                _ => FixedReply(profile),
            };
            if (string.Equals(failure, "late", StringComparison.Ordinal)) { f.OnObserve = () => f.QueueTime.Ticks += TimeSpan.FromMinutes(5).Ticks; }
            await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue) with { QueueProfile = profile, QueueTarget = FixedTarget(profile) },
                RequestOrigin.LocalUi, () => true, f.Token);
            var reply = await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueDispatch) with { QueueRevision = 1 },
                RequestOrigin.LocalUi, () => true, f.Token);
            var receipt = reply.QueueDispatch!.Single();
            receipt.Entry.State.Should().Be(SessionQueueState.Failed);
            receipt.Result.Should().BeNull();
            receipt.Version.Should().BeNull();
            f.QueueRows.Single().Result.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_deadline_is_rechecked_at_commit_and_final_publication(bool afterCommit)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        f.QueueReply = FixedReply(SessionQueueReadProfile.Readiness);
        if (afterCommit) { f.AfterComplete = () => f.QueueTime.Ticks += TimeSpan.FromMinutes(5).Ticks; }
        else { f.BeforeComplete = () => f.QueueTime.Ticks += TimeSpan.FromMinutes(5).Ticks; }
        await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue) with { QueueProfile = SessionQueueReadProfile.Readiness },
            RequestOrigin.LocalUi, () => true, f.Token);
        var dispatch = () => queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueDispatch) with { QueueRevision = 1 },
            RequestOrigin.LocalUi, () => true, f.Token);
        await dispatch.Should().ThrowAsync<InvalidOperationException>();
        f.QueueRows.Single().State.Should().Be(afterCommit ? SessionQueueState.Succeeded : SessionQueueState.Running,
            "a physical on-time commit is not relabelled, but no late output is delivered");
    }

    [Fact]
    public async Task Exhausted_commit_to_invoke_budget_refuses_the_actual_read_and_records_failure()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        f.AdmissionReceipt = entry => { f.QueueTime.Ticks += TimeSpan.FromMinutes(5).Ticks; return entry; };
        await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue) with { QueueProfile = SessionQueueReadProfile.Readiness },
            RequestOrigin.LocalUi, () => true, f.Token);
        var receipt = (await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueDispatch) with { QueueRevision = 1 },
            RequestOrigin.LocalUi, () => true, f.Token)).QueueDispatch!.Single();
        f.QueueActions.Should().Be(0);
        receipt.Entry.State.Should().Be(SessionQueueState.Failed);
        receipt.Result.Should().BeNull();
    }

    [Fact]
    public void Bounded_manual_batch_omits_only_large_retained_results_with_an_exact_status_recovery_marker()
    {
        using var f = new Fixture();
        var entry = new SessionQueueEntry(f.Request, new(1), new(1), 1, SessionQueueState.Succeeded,
            f.QueueRun, 1, f.QueueTime.GetUtcNow(), f.QueueTime.GetUtcNow().AddMinutes(30));
        var receipts = Enumerable.Range(0, 32).Select(index =>
            new SessionQueueDispatchReceipt(entry, null, index == 31 ? null
                : FixedReply(SessionQueueReadProfile.Readiness) with { Reason = new string('x', 3000) })).ToArray();
        var reply = new SessionCommandResult("committed", "Read exact status for deferred results.") { QueueDispatch = receipts };
        SessionCommandResult.GetSerializedSize(reply).Should().BeGreaterThan(SessionCommand.MaximumResultBytes);
        var bounded = SessionQueueService.BoundDispatchResults(reply);
        SessionCommandResult.Serialize(bounded).Length.Should().BeLessThanOrEqualTo(SessionCommand.MaximumResultBytes);
        bounded.QueueDispatch.Should().HaveCount(32);
        bounded.QueueDispatch!.Should().Contain(receipt => receipt.ResultAvailableByExactStatus && receipt.Result == null);
        bounded.QueueDispatch.Should().Contain(receipt => receipt.Result != null && !receipt.ResultAvailableByExactStatus);
        SessionQueueService.BoundDispatchResults(new("committed", "empty") { QueueDispatch = [] }).QueueDispatch.Should().BeEmpty();
    }

    private static CapabilityReply FixedReply(SessionQueueReadProfile profile) => profile switch
    {
        SessionQueueReadProfile.Version => new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0.0", "fixed")),
        SessionQueueReadProfile.Capabilities => new(CapabilityOutcome.Succeeded, "catalogue", Capabilities: new(ReadOnlyCapabilityCatalog.Descriptors, 6, null)),
        SessionQueueReadProfile.Readiness => new(CapabilityOutcome.Succeeded, "recorded-observations", Readiness: new([], 0, null)),
        SessionQueueReadProfile.Capability => new(CapabilityOutcome.Succeeded, "admitted-read-only", Descriptor: FixedTarget(profile)!.Descriptor),
        SessionQueueReadProfile.RuntimeStatus => new(CapabilityOutcome.Succeeded, "recorded-observation",
            Runtime: new(ReadOnlyCapabilityCatalog.LocalRuntimeId, RuntimeLocality.Local, CapabilityAvailability.NotObserved, null, "not observed", null, false)),
        _ => new(CapabilityOutcome.Succeeded, "recorded-observation", Runtimes: new([], 0, null)),
    };
    private static SessionQueueCapabilityTarget? FixedTarget(SessionQueueReadProfile profile) =>
        profile == SessionQueueReadProfile.Capability ? new(ReadOnlyCapabilityCatalog.Version) : null;
}
