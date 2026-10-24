using System.Reflection;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.UnitTests.Agents;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

[Collection("Host tracing")]
public sealed class ModelTurnHostAgentLedgerTests
{
    [Theory]
    [InlineData("unqualified")]
    [InlineData("proof")]
    [InlineData("producer")]
    [InlineData("ordinary")]
    [InlineData("capacity-changed")]
    [InlineData("existing-revoked")]
    public async Task FinalDispatchRevalidatesBothExactOwners(string fault)
    {
        await using var f = new AgentRunSetTests.Fixture();
        await using var runs = f.Factory.Create();
        await f.Review(runs, 1);
        await f.Review(runs, 2);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var first = await AgentRunSetBoundaryTests.Admit(f, runs.Read(1)!.Review!.Input);
        var one = f.Host.IssueLocalAgentLane(runs.Read(1)!.Review!, first, f.Qualification);
        var firstLease = AgentRunSetBoundaryTests.Acquire(f.Host, first)!;
        try
        {
            var review = runs.Read(2)!.Review!;
            var request = new HostRequest(first.Provenance.Request.RequestId, review.Target.Authority.SessionId,
                first.Provenance.Request.TaskId, first.Provenance.Request.Origin);
            var context = new ModelContextEnvelope(request, "policy", review.Input, [], f.Time.Now,
                f.Time.Now.AddMinutes(1), ModelToolMode.None, review.Profile.Constraints);
            var second = new ModelTurn(first.Owner, first.Issuer, first.Provenance with { Request = request },
                context, context.Serialize(), review.Target.Authority.Generation, new(1), 0, first.Registration);
            var producer = fault is "producer" ? new AgentRunSetTests.Qualification(f.Time, f.Adapter) : f.Qualification;
            if (fault is not "ordinary") { f.Host.IssueLocalAgentLane(review, second, producer); }
            if (fault is "unqualified") { f.Qualification.Proof = null; }
            if (fault is "proof") { f.Qualification.Proof = f.Qualification.Proof! with { Revision = Guid.NewGuid() }; }
            if (fault is "capacity-changed") { f.Qualification.Proof = f.Qualification.Proof! with { MaximumRuns = 1 }; }
            if (fault is "existing-revoked") { one.Capacity = null; }
            AgentRunSetBoundaryTests.Acquire(f.Host, second).Should().BeNull();
            f.Adapter.Active.Should().Be(0);
            f.Host.IsQuiescent.Should().BeFalse();
        }
        finally { AgentRunSetBoundaryTests.Release(f.Host, firstLease); }
        f.Host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownAdapterQuiescenceAndDisposedHostNeverAcquireOrLeakSlot()
    {
        await using var f = new AgentRunSetTests.Fixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var turn = await AgentRunSetBoundaryTests.Admit(f, "original");
        f.Adapter.Quiescent = false;
        AgentRunSetBoundaryTests.Acquire(f.Host, turn).Should().BeNull();
        f.Adapter.Quiescent = true;
        await f.Host.DisposeAsync();
        AgentRunSetBoundaryTests.Acquire(f.Host, turn).Should().BeNull();
    }

    [Fact]
    public async Task CapacityProducerCannotMutateLedgerDuringAvailabilityObservation()
    {
        await using var f = new AgentRunSetTests.Fixture();
        await using var runs = f.Factory.Create();
        await f.Review(runs, 1);
        await f.Review(runs, 2);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var turn = await AgentRunSetBoundaryTests.Admit(f, runs.Read(1)!.Review!.Input);
        f.Host.IssueLocalAgentLane(runs.Read(1)!.Review!, turn, f.Qualification);
        var lease = AgentRunSetBoundaryTests.Acquire(f.Host, turn)!;
        f.Qualification.OnCurrent = () => AgentRunSetBoundaryTests.Release(f.Host, lease);
        f.Host.CanSubmitLocalAgent(runs.Read(2)!.Review!, f.Qualification).Should().BeFalse();
        f.Host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task CloseReportsCancellationFailureWhileDisposingEveryAdapter()
    {
        var f = new AgentRunSetTests.Fixture();
        // The host lifetime is intentionally private; inject a throwing subscriber at this cancellation seam.
        var lifetime = (CancellationTokenSource)typeof(ModelTurnHost)
            .GetField("lifetime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Host)!;
        using var registration = lifetime.Token.Register(() => throw new IOException("Cancellation failed."));
        await f.Host.Invoking(value => value.DisposeAsync().AsTask()).Should().ThrowAsync<AggregateException>();
        f.Adapter.Disposed.Should().BeTrue();
        await f.Invoking(value => value.DisposeAsync().AsTask()).Should().ThrowAsync<AggregateException>();
    }
}
