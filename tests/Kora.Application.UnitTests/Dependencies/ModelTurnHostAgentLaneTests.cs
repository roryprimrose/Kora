using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.UnitTests.Agents;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

[Collection("Host tracing")]
public sealed class ModelTurnHostAgentLaneTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DisposalAggregatesEveryAdapterFailureWithoutSkippingLaterResources()
    {
        var other = new AgentRunSetTests.BarrierAdapter { FailDispose = true };
        var f = new AgentRunSetTests.Fixture(other);
        f.Adapter.FailDispose = true;
        var failure = await f.Host.Invoking(value => value.DisposeAsync().AsTask()).Should().ThrowAsync<AggregateException>();
        failure.Which.Flatten().InnerExceptions.Should().HaveCount(2);
        f.Adapter.Disposed.Should().BeTrue();
        other.Disposed.Should().BeTrue();
        await f.Invoking(value => value.DisposeAsync().AsTask()).Should().ThrowAsync<AggregateException>();
    }

    [Fact]
    public async Task QualificationCallbackCannotDispatchAfterReentrantHostClosureOrLeakQuiescence()
    {
        await using var f = new AgentRunSetTests.Fixture();
        await using var runs = f.Factory.Create();
        await f.Review(runs, 1);
        Task? closing = null;
        f.Qualification.OnObserve = () => closing = f.Host.DisposeAsync().AsTask();
        await runs.InvokeAsync(1, RequestOrigin.LocalUi, Token);
        closing.Should().NotBeNull();
        await closing!;
        f.Adapter.Active.Should().Be(0);
        f.Host.IsQuiescent.Should().BeTrue();
        runs.Read(1)!.Receipt!.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        runs.Read(1)!.Receipt!.Message.Reason.Should().Be(ModelTurnReason.ProviderBusy);
        f.Readers[0].Released.Should().BeTrue();
    }

    [Fact]
    public async Task CancellationRetainsOnlyExactLaneAndSourceUntilLateCompletionAndNeverCancelsOtherRun()
    {
        await using var f = new AgentRunSetTests.Fixture();
        await using var runs = f.Factory.Create();
        await f.Review(runs, 1);
        await f.Review(runs, 2);
        var one = runs.InvokeAsync(1, RequestOrigin.LocalUi, Token);
        await f.Adapter.Started(f.Sessions[0]).Task;
        var two = runs.InvokeAsync(2, RequestOrigin.LocalUi, Token);
        await f.Adapter.Started(f.Sessions[1]).Task;
        var stop = runs.StopAsync(1);
        await f.Adapter.Cancelled(f.Sessions[0]).Task;
        f.Adapter.Cancelled(f.Sessions[1]).Task.IsCompleted.Should().BeFalse();
        one.IsCompleted.Should().BeFalse();
        stop.IsCompleted.Should().BeFalse();
        f.Readers.Should().OnlyContain(item => !item.Released);
        f.Adapter.Active.Should().Be(2);
        await using var replacement = f.Factory.Create();
        await replacement.ReviewAsync(1, "another", f.Sessions[0], "replacement", RequestOrigin.LocalUi, () => true, Token);
        await replacement.Invoking(value => value.InvokeAsync(1, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Active.Should().Be(2);
        f.Adapter.Finish(f.Sessions[0], "forbidden late answer");
        await one.Invoking(value => value).Should().ThrowAsync<Exception>();
        await stop.Invoking(value => value).Should().ThrowAsync<AggregateException>();
        f.Readers[0].Released.Should().BeTrue();
        f.Readers[1].Released.Should().BeFalse();
        f.Messages.Values.Single(item => item.Request.SessionId == f.Sessions[0].Authority.SessionId)
            .AssistantResponse.Should().BeNull();
        f.Adapter.Cancelled(f.Sessions[1]).Task.IsCompleted.Should().BeFalse();
        f.Adapter.Finish(f.Sessions[1], "other run remains genuine");
        await two;
        runs.Read(2)!.Receipt!.Message.AssistantResponse!.Body.Should().Be("other run remains genuine");
        await replacement.Invoking(value => value.StopAsync(1)).Should().ThrowAsync<AggregateException>();
    }

    [Fact]
    public async Task GlobalCloseJoinsBothIgnoringAdaptersAndAggregatesBothFailuresAfterReleasingSources()
    {
        var f = new AgentRunSetTests.Fixture();
        var runs = f.Factory.Create();
        await f.Review(runs, 1);
        await f.Review(runs, 2);
        var one = runs.InvokeAsync(1, RequestOrigin.LocalUi, Token);
        await f.Adapter.Started(f.Sessions[0]).Task;
        var two = runs.InvokeAsync(2, RequestOrigin.LocalUi, Token);
        await f.Adapter.Started(f.Sessions[1]).Task;
        var closingRuns = runs.DisposeAsync().AsTask();
        await f.Adapter.Cancelled(f.Sessions[0]).Task;
        await f.Adapter.Cancelled(f.Sessions[1]).Task;
        var closingHost = f.Host.DisposeAsync().AsTask();
        closingRuns.IsCompleted.Should().BeFalse();
        closingHost.IsCompleted.Should().BeFalse();
        f.Adapter.Fail(f.Sessions[0]);
        await one.Invoking(value => value).Should().ThrowAsync<Exception>();
        closingRuns.IsCompleted.Should().BeFalse();
        closingHost.IsCompleted.Should().BeFalse();
        f.Readers[0].Released.Should().BeTrue();
        f.Readers[1].Released.Should().BeFalse();
        f.Adapter.Fail(f.Sessions[1]);
        await two.Invoking(value => value).Should().ThrowAsync<Exception>();
        var resources = await closingHost.Invoking(value => value).Should().ThrowAsync<AggregateException>();
        resources.Which.Flatten().InnerExceptions.Should().HaveCount(2);
        var runFailures = await closingRuns.Invoking(value => value).Should().ThrowAsync<AggregateException>();
        runFailures.Which.Flatten().InnerExceptions.Should().HaveCount(2);
        f.Readers.Should().OnlyContain(item => item.Released);
        f.Host.IsQuiescent.Should().BeTrue();
        f.Messages.Values.Should().OnlyContain(item => item.AssistantResponse == null);
        await f.Invoking(value => value.DisposeAsync().AsTask()).Should().ThrowAsync<AggregateException>();
    }

    [Fact]
    public async Task OrdinaryTurnCannotOverlapLocalAgentAndTwoLocalLanesRejectThirdDistinctSession()
    {
        await using var f = new AgentRunSetTests.Fixture();
        await using var runs = f.Factory.Create();
        await f.Review(runs, 1);
        await f.Review(runs, 2);
        var one = runs.InvokeAsync(1, RequestOrigin.LocalUi, Token);
        await f.Adapter.Started(f.Sessions[0]).Task;
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        // The ordinary request has valid task authority, but never a host-issued agent lane.
        var request = new HostRequest(new(Guid.NewGuid()), f.Sessions[2].Authority.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using (HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            f.Tasks[request.TaskId] = new(new(request, new(1), HostTaskState.IntentRecorded), new(1), "host", true, null);
            var context = new ModelContextEnvelope(request, "policy", "ordinary request", [], f.Time.Now, f.Time.Now.AddMinutes(1));
            var admission = await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token);
            admission.Turn.Should().NotBeNull();
            (await f.Host.RunAsync(admission.Turn!, Token)).Reason.Should().Be(ModelTurnReason.ProviderBusy);
        }
        var two = runs.InvokeAsync(2, RequestOrigin.LocalUi, Token);
        await f.Adapter.Started(f.Sessions[1]).Task;
        await using var third = f.Factory.Create();
        await third.ReviewAsync(1, "third", f.Sessions[2], "third original request", RequestOrigin.LocalUi, () => true, Token);
        await third.Invoking(value => value.InvokeAsync(1, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Active.Should().Be(2);
        f.Adapter.Finish(f.Sessions[0], "one");
        await one;
        // Stale capacity no longer permits new dispatch, but does not prevent an already-owned genuine terminal retention.
        f.Qualification.Current = false;
        f.Adapter.Finish(f.Sessions[1], "two");
        await two;
        runs.Read(2)!.Receipt!.Message.AssistantResponse!.Body.Should().Be("two");
        await third.Invoking(value => value.StopAsync(1)).Should().ThrowAsync<AggregateException>();
    }
}
