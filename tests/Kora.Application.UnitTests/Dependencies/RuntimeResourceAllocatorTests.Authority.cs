using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Application.UnitTests.Agents;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class RuntimeResourceAllocatorTests
{
    [Fact]
    public async Task IssuerRefusesForeignConstructedReplayedAndCrossOwnerReleaseCapabilities()
    {
        using var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var owner = Guid.NewGuid();
        var allocator = new RuntimeResourceAllocator(owner, f.Time, f, source);
        var turn = ResourceTurn(owner, source);
        (await Allocate(allocator, ResourceTurn(owner, source), () => false)).Should().BeNull();
        var budget = new ConversationExecutionBudget(ConversationExecutionLimit.Default, 0, false);
        var forged = new RuntimeResourceAllocator.Lease { Turn = turn, Evidence = source.Evidence, Budget = budget };
        allocator.IsCurrent(forged).Should().BeFalse();
        allocator.Invoking(value => value.Join(forged, false)).Should().Throw<InvalidOperationException>();
        await Allocate(allocator, ResourceTurn(Guid.NewGuid(), source)).Invoking(value => value)
            .Should().ThrowAsync<InvalidOperationException>();
        var lease = (await Allocate(allocator, turn))!;
        allocator.IsCurrent(lease).Should().BeTrue();
        allocator.IsCurrent(new() { Turn = turn, Evidence = source.Evidence, Budget = budget }).Should().BeFalse();
        allocator.Invoking(value => value.Join(new() { Turn = turn, Evidence = source.Evidence, Budget = budget }, false))
            .Should().Throw<InvalidOperationException>();
        await Allocate(allocator, turn).Invoking(value => value).Should().ThrowAsync<InvalidOperationException>();
        new RuntimeResourceAllocator(owner, f.Time, f, source).IsCurrent(lease).Should().BeFalse();
        allocator.Join(lease, false);
        allocator.Join(lease, false);
        lease.Joined.Should().BeTrue();
        allocator.IsIdle.Should().BeTrue();
    }

    [Fact]
    public async Task TenBoundedWaitersAreFifoAndOverflowOrDuplicateSessionsCannotStealReleasedBudget()
    {
        using var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var owner = Guid.NewGuid();
        var allocator = new RuntimeResourceAllocator(owner, f.Time, f, source);
        var first = (await Allocate(allocator, ResourceTurn(owner, source)))!;
        var second = (await Allocate(allocator, ResourceTurn(owner, source)))!;
        var turns = Enumerable.Range(0, RuntimeResourceAllocator.MaximumWaiters).Select(_ => ResourceTurn(owner, source)).ToArray();
        var queued = turns.Select(turn => Allocate(allocator, turn)).ToArray();
        queued.Should().OnlyContain(item => !item.IsCompleted);
        allocator.OwnsSession(turns[0].Provenance.Request.SessionId).Should().BeTrue();
        (await Allocate(allocator, ResourceTurn(owner, source))).Should().BeNull();
        var duplicate = ResourceTurn(owner, source, turns[0].Provenance.Request.SessionId);
        (await Allocate(allocator, duplicate)).Should().BeNull();
        var idle = allocator.WaitForQuiescenceAsync(Token);
        idle.IsCompleted.Should().BeFalse();
        allocator.Join(first, false);
        for (var index = 0; index < queued.Length; index++)
        {
            var next = (await queued[index].WaitAsync(TimeSpan.FromSeconds(5), Token))!;
            next.Turn.Should().BeSameAs(turns[index]);
            queued.Skip(index + 1).Should().OnlyContain(item => !item.IsCompleted);
            allocator.Join(next, false);
        }
        idle.IsCompleted.Should().BeFalse();
        allocator.Join(second, false);
        await idle;
        allocator.OwnsSession(turns[0].Provenance.Request.SessionId).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WaitDeadlineAndOriginalContextExpiryReleaseOnlyWaitingOwnership(bool shortContext)
    {
        using var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var time = new CapacityTime();
        var owner = Guid.NewGuid();
        var allocator = new RuntimeResourceAllocator(owner, time, f, source);
        var first = (await Allocate(allocator, ResourceTurn(owner, source)))!;
        var second = (await Allocate(allocator, ResourceTurn(owner, source)))!;
        var turn = ResourceTurn(owner, source, lifetime: shortContext ? TimeSpan.FromSeconds(2) : TimeSpan.FromMinutes(1));
        var waiting = Allocate(allocator, turn);
        time.Due.Should().Be(shortContext ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(15));
        time.Fire();
        (await waiting).Should().BeNull();
        allocator.OwnsSession(turn.Provenance.Request.SessionId).Should().BeFalse();
        first.Joined.Should().BeFalse();
        second.Joined.Should().BeFalse();
        allocator.Join(first, false);
        allocator.Join(second, false);
    }

    [Theory]
    [InlineData("request-audit")]
    [InlineData("success-audit")]
    [InlineData("admission-request")]
    [InlineData("admission-success")]
    [InlineData("qualification-request")]
    [InlineData("qualification-success")]
    public async Task RequiredIssuanceAuditAndFinalAuthorityFencesCannotLeakALease(string fault)
    {
        using var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var audit = new CapacityAudit();
        var owner = Guid.NewGuid();
        var allocator = new RuntimeResourceAllocator(owner, f.Time, audit, source);
        var live = true;
        audit.OnWrite = entry =>
        {
            var atBoundary = fault.EndsWith("request", StringComparison.Ordinal) && entry.Outcome == SecurityAuditOutcome.Requested
                || fault.EndsWith("success", StringComparison.Ordinal) && entry.Outcome == SecurityAuditOutcome.Succeeded;
            if (fault is "request-audit" && entry.Outcome == SecurityAuditOutcome.Requested
                || fault is "success-audit" && entry.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("required resource audit unavailable"); }
            if (atBoundary && fault.StartsWith("admission-", StringComparison.Ordinal)) { live = false; }
            if (atBoundary && fault.StartsWith("qualification-", StringComparison.Ordinal)) { source.Current = false; }
        };
        var pending = Allocate(allocator, ResourceTurn(owner, source), () => live);
        if (fault is "request-audit" or "success-audit")
        { await pending.Invoking(value => value).Should().ThrowAsync<IOException>(); }
        else { (await pending).Should().BeNull(); }
        allocator.IsIdle.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReleaseAuditFailureQuarantinesExactResourceWithoutReleasingUnrelatedResource(bool legacy)
    {
        using var source = new RuntimeSource { Current = !legacy };
        await using var f = new AgentRunSetTests.Fixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var audit = new CapacityAudit();
        var owner = Guid.NewGuid();
        var allocator = new RuntimeResourceAllocator(owner, f.Time, audit, source);
        var lease = (await Allocate(allocator, ResourceTurn(owner, source)))!;
        audit.OnWrite = entry =>
        {
            if (entry.ActionId.StartsWith("runtime.resource.release", StringComparison.Ordinal)) { throw new IOException("release unconfirmed"); }
        };
        allocator.Invoking(value => value.Join(lease, false)).Should().Throw<IOException>();
        lease.FailedJoin.Should().BeTrue();
        allocator.IsCurrent(lease).Should().BeFalse();
        allocator.Join(lease, false);
        audit.OnWrite = null;
        (await Allocate(allocator, ResourceTurn(owner, source))).Should().BeNull();
        if (!legacy)
        {
            source.Current = false;
            (await Allocate(allocator, ResourceTurn(owner, source))).Should().BeNull();
            source.Current = true;
            source.Evidence = source.Evidence with { Resources = [new(new(Guid.NewGuid()), false)] };
            var independent = (await Allocate(allocator, ResourceTurn(owner, source)))!;
            allocator.Join(independent, false);
        }
    }

    [Fact]
    public async Task UnqualifiedRevokedAndExpiredWaitingRequestsCannotIssueCapacity()
    {
        using var source = new RuntimeSource();
        await using var f = new AgentRunSetTests.Fixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var time = new CapacityTime();
        var owner = Guid.NewGuid();
        var allocator = new RuntimeResourceAllocator(owner, time, f, source);
        var unqualified = ResourceTurn(owner, source);
        unqualified.Registration = unqualified.Registration with { Evidence = ModelQualificationGate.None };
        (await Allocate(allocator, unqualified)).Should().BeNull();
        var first = (await Allocate(allocator, ResourceTurn(owner, source)))!;
        var second = (await Allocate(allocator, ResourceTurn(owner, source)))!;
        time.Now = DateTimeOffset.UnixEpoch.AddMinutes(2);
        (await Allocate(allocator, ResourceTurn(owner, source))).Should().BeNull();
        allocator.Join(first, false);
        allocator.Join(second, false);
        await source.Revocation.CancelAsync();
        (await Allocate(allocator, ResourceTurn(owner, source))).Should().BeNull();
    }

    private static ModelTurn ResourceTurn(Guid owner, RuntimeSource source, HostId<SessionIdentity>? session = null,
        TimeSpan? lifetime = null)
    {
        var request = new HostRequest(new(Guid.NewGuid()), session ?? new(Guid.NewGuid()), new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var adapter = source.Transports.GetOrAdd(request.SessionId, _ => new());
        var context = new ModelContextEnvelope(request, "policy", "original", [], DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.Add(lifetime ?? TimeSpan.FromMinutes(1)));
        return new(owner, HostActivity.RequireCurrent(), new(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate),
            context, context.Serialize(), new(1), new(1), 0,
            new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                DateTimeOffset.UnixEpoch.AddMinutes(5), adapter))
        { Profile = Profile };
    }

    private static Task<RuntimeResourceAllocator.Lease?> Allocate(RuntimeResourceAllocator allocator, ModelTurn turn,
        Func<bool>? admitted = null) => allocator.AcquireAsync(turn, admitted ?? (() => true), Token);

    private sealed class CapacityAudit : ISecurityAuditLog
    {
        internal Action<SecurityAuditEvent>? OnWrite { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            HostActivity.RequireCurrent();
            OnWrite?.Invoke(auditEvent);
        }
    }

    private sealed class CapacityTime : TimeProvider
    {
        private TimerCallback? callback;
        private object? state;
        internal TimeSpan Due { get; private set; }
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback;
            this.state = state;
            Due = dueTime;
            return new CapacityTimer();
        }
        internal void Fire() => callback!(state);
        private sealed class CapacityTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}