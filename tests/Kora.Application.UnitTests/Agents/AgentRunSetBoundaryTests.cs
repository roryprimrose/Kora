using System.Reflection;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Agents;

[Collection("Host tracing")]
public sealed class AgentRunSetBoundaryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReferenceResolutionHasItsOwnExplicitTransitionAndStopRemainsIdempotent()
    {
        await using var f = new AgentRunSetTests.Fixture();
        f.ConfigureReader = reader => reader.Text = reader.Text
            .Replace("kora-agent-format: 1", "kora-agent-format: 2", StringComparison.Ordinal)
            .Replace("references: none", "references: disk.kora-profile.instruction.example", StringComparison.Ordinal);
        await using var runs = f.Factory.Create();
        await f.Review(runs, 1);
        runs.Read(1)!.Status.Should().Be(AgentRunSet.AgentRunStatus.Reviewed);
        await runs.ResolveAsync(1, RequestOrigin.LocalUi, Token);
        runs.Read(1)!.Status.Should().Be(AgentRunSet.AgentRunStatus.Ready);
        f.Instructions.Store.Reads.Should().Be(2);
        f.Adapter.Active.Should().Be(0);
        runs.Revoke(2);
        runs.Revoke(1);
        Action stopped = () => _ = runs.InvokeAsync(1, RequestOrigin.LocalUi, Token);
        stopped.Should().Throw<InvalidOperationException>();
        await runs.DisposeAsync();
        await runs.StopAsync(1);
        f.Readers[0].Released.Should().BeTrue();
    }

    [Fact]
    public async Task CancellationCallbackFailureStillJoinsAndReleasesSource()
    {
        await using var f = new AgentRunSetTests.Fixture();
        var runs = f.Factory.Create();
        await f.Review(runs, 1);
        // Inject a throwing subscriber on the exact private run token, not a disposed review's linked token.
        var entries = (System.Collections.IDictionary)typeof(AgentRunSet)
            .GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runs)!;
        var entry = entries[1]!;
        var cancellation = (CancellationTokenSource)entry.GetType()
            .GetProperty("Cancellation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry)!;
        using var registration = cancellation.Token.Register(() => throw new IOException("Cancellation callback failed."));
        await runs.Invoking(value => value.StopAsync(1)).Should().ThrowAsync<AggregateException>();
        f.Readers[0].Released.Should().BeTrue();
        await runs.DisposeAsync();
    }

    [Fact]
    public async Task FailedReviewCannotResolveOrPublishAndUnboundAdmissionHasNoAuthority()
    {
        await using var f = new AgentRunSetTests.Fixture();
        f.ConfigureReader = reader => reader.Text = "invalid";
        var runs = f.Factory.Create();
        await runs.Invoking(value => f.Review(value, 1)).Should().ThrowAsync<Exception>();
        await runs.Invoking(value => value.ResolveAsync(1, RequestOrigin.LocalUi, Token)).Should().ThrowAsync<InvalidOperationException>();
        await runs.Invoking(value => value.DisposeAsync().AsTask()).Should().ThrowAsync<AggregateException>();
        var admission = new AgentLaneModelAdmission(f.Host, f.Qualification);
        admission.IsQuiescent.Should().BeFalse();
        await admission.WaitForQuiescenceAsync(Token);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("provider")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("origin")]
    [InlineData("input")]
    [InlineData("tools")]
    [InlineData("constraints")]
    [InlineData("calls")]
    [InlineData("wire")]
    public async Task LaneIssuanceRejectsEveryMismatchWithoutDispatch(string fault)
    {
        await using var f = new AgentRunSetTests.Fixture();
        await using var runs = f.Factory.Create();
        await f.Review(runs, 1);
        var review = runs.Read(1)!.Review!;
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var request = new HostRequest(new(Guid.NewGuid()), review.Target.Authority.SessionId,
            new(Guid.NewGuid()), review.Origin);
        var context = new ModelContextEnvelope(request, "policy", fault is "input" ? "different" : review.Input,
            [], f.Time.Now, f.Time.Now.AddMinutes(1),
            fault is "tools" or "calls" ? ModelToolMode.ReadOnly : ModelToolMode.None,
            fault is "constraints" ? null : fault is "calls"
                ? new(1, [ReadOnlyCapabilityCatalog.List], 1, 1, 32768, 4096, 15000) : null);
        if (fault is not ("tools" or "constraints" or "calls"))
        {
            context = new(request, "policy", context.UserRequest, [], f.Time.Now, f.Time.Now.AddMinutes(1),
                ModelToolMode.None, review.Profile.Constraints);
        }
        var canonical = await Admit(f, review.Input);
        var provenance = canonical.Provenance with
        {
            Request = new(request.RequestId,
                fault is "session" ? f.Sessions[1].Authority.SessionId : request.SessionId, request.TaskId,
                fault is "origin" ? RequestOrigin.ActivatedVoice : request.Origin),
            Selection = fault is "provider" ? ModelProviderSelection.CopilotCandidate : canonical.Provenance.Selection,
        };
        var turn = new ModelTurn(fault is "owner" ? Guid.NewGuid() : canonical.Owner,
            HostActivity.RequireCurrent(), provenance, context,
            fault is "wire" ? [] : context.Serialize(),
            fault is "generation" ? new(2) : review.Target.Authority.Generation,
            new(1), 0, canonical.Registration);
        Action issue = () => f.Host.IssueLocalAgentLane(review, turn, f.Qualification);
        issue.Should().Throw<InvalidOperationException>();
        f.Adapter.Active.Should().Be(0);
        f.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task AdmissionCannotBeReboundReplayedOrRunWithAnotherTurn()
    {
        await using var f = new AgentRunSetTests.Fixture();
        await using var runs = f.Factory.Create();
        await f.Review(runs, 1);
        var review = runs.Read(1)!.Review!;
        var lane = new AgentLaneModelAdmission(f.Host, f.Qualification);
        lane.Bind(review);
        Action rebind = () => lane.Bind(review);
        rebind.Should().Throw<InvalidOperationException>();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var turn = await Admit(f, review.Input);
        using var requestRoot = HostActivity.BeginRoot(turn.Context.Request,
            HostActivityLayer.Application, HostOperation.Request);
        Action unissued = () => _ = lane.RunAsync(turn, () => true, Token);
        unissued.Should().Throw<InvalidOperationException>();
        await lane.Invoking(value => value.AdmitAsync(ModelTurnChoice.Hosted, turn.Context, null, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        var policy = new ModelProviderPolicy(turn.Context.Request.SessionId, new(1), ModelProviderMode.LocalOnly,
            ModelProviderSelection.OllamaCandidate, ModelProviderSelection.CopilotCandidate);
        var question = new HostQuestionRecord(new(turn.Context.Request, new(Guid.NewGuid()), new(1)),
            new("Confirm?", QuestionKind.Text, [], maximumTextLength: 20), new(1), f.Time.Now.AddMinutes(1));
        var handoff = new ModelHandoffReview(new(f.Host, policy, turn.Context,
            ModelHandoffReason.UserRequestedHosted, question, new(1), new(1), 0));
        await lane.Invoking(value => value.AdmitAsync(ModelTurnChoice.Local, turn.Context, handoff, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        var unbound = new AgentLaneModelAdmission(f.Host, f.Qualification);
        await unbound.Invoking(value => value.AdmitAsync(ModelTurnChoice.Local, turn.Context, null, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        var admitted = await lane.AdmitAsync(ModelTurnChoice.Local, turn.Context, null, Token);
        admitted.Turn.Should().NotBeNull();
        Action wrongTurn = () => _ = lane.RunAsync(turn, () => true, Token);
        wrongTurn.Should().Throw<InvalidOperationException>();
        await lane.Invoking(value => value.AdmitAsync(ModelTurnChoice.Local, turn.Context, null, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await f.Host.DisposeAsync();
        f.Host.CanSubmitLocalAgent(review, f.Qualification).Should().BeFalse();
    }

    internal static async Task<ModelTurn> Admit(AgentRunSetTests.Fixture f, string input)
    {
        var request = new HostRequest(new(Guid.NewGuid()), f.Sessions[0].Authority.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        f.Tasks[request.TaskId] = new(new(request, new(1), HostTaskState.IntentRecorded), new(1), "host", true, null);
        var context = new ModelContextEnvelope(request, "policy", input, [], f.Time.Now,
            f.Time.Now.AddMinutes(1), ModelToolMode.None, new(1, [], 0, 1, 32768, 4096, 15000));
        return (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, context, Token)).Turn!;
    }

    // The ledger is private and public admission rejects stale turns before reaching it.
    // Exercise its final defensive acquisition boundary without replacing host authority.
    internal static object? Acquire(ModelTurnHost host, ModelTurn turn) =>
        typeof(ModelTurnHost).GetMethod("AcquireDispatch", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, [turn]);

    internal static void Release(ModelTurnHost host, object lease) =>
        typeof(ModelTurnHost).GetMethod("ReleaseDispatch", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(host, [lease]);
}
