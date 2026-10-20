using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("failed")]
    [InlineData("malformed")]
    public async Task AdapterRejectsMissingFailedOrMalformedCallbackResultsWithoutAnotherProviderExchange(string fault)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var transport = new ToolTransport(VersionCall, FinalAnswer);
        await using var loop = CreateLoop(fixture, transport);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), fixture.Request, ModelProviderSelection.OllamaCandidate);
        var result = await loop.RunAsync(provenance, null, fixture.Context().Serialize(),
            (_, _, _) => ValueTask.FromResult(fault switch
            {
                "null" => null!,
                "failed" => new CapabilityReply(CapabilityOutcome.Failed, "not-observed"),
                _ => new CapabilityReply(CapabilityOutcome.Succeeded, "invalid-shape"),
            }), _ => ValueTask.FromResult(ModelTurnReason.None), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Reason.Should().Be(ModelTurnReason.ToolResultRejected);
        result.Response.Should().BeNull();
        transport.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("before-tool")]
    [InlineData("undefined-reason")]
    [InlineData("before-check")]
    [InlineData("after-check")]
    [InlineData("clock-backward")]
    public async Task AdapterRechecksExactLiveHostAndFiniteTimeBeforeDispatchingAnyTool(string fault)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var transport = new ToolTransport(VersionCall, FinalAnswer);
        if (fault is "before-check") { transport.AfterRequest = _ => HostActivity.RequireCurrent().Complete(HostOperationOutcome.Completed); }
        if (fault is "clock-backward") { transport.AfterRequest = _ => fixture.Time.Now = fixture.Time.Now.AddSeconds(-1); }
        await using var loop = CreateLoop(fixture, transport);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), fixture.Request, ModelProviderSelection.OllamaCandidate);
        var validations = 0;
        var invocations = 0;
        var result = await loop.RunAsync(provenance, null, fixture.Context().Serialize(),
            (_, _, _) =>
            {
                invocations++;
                return ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "unobserved")));
            }, _ =>
            {
                validations++;
                if (fault is "after-check") { HostActivity.RequireCurrent().Complete(HostOperationOutcome.Completed); }
                return ValueTask.FromResult(fault is "undefined-reason" ? (ModelTurnReason)999
                    : fault is "before-tool" && validations == 3 ? ModelTurnReason.HostAdmissionClosed : ModelTurnReason.None);
            }, Token);
        result.Outcome.Should().Be(fault is "clock-backward" ? ModelTurnOutcome.TimedOut : ModelTurnOutcome.Denied);
        result.Response.Should().BeNull();
        invocations.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdapterRejectsCompleteMalformedOrOverheadExhaustedContextBeforeTransport(bool exhausted)
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var transport = new ToolTransport(FinalAnswer);
        await using var loop = CreateLoop(fixture, transport);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), fixture.Request, ModelProviderSelection.OllamaCandidate);
        var emptyBytes = fixture.Context(text: "").Serialize().Length;
        var wire = exhausted ? fixture.Context(text: new string('x', ModelToolProtocol.MaximumInputUtf8Bytes - emptyBytes)).Serialize()
            : "{}"u8.ToArray();
        var result = await loop.RunAsync(provenance, null, wire,
            (_, _, _) => throw new InvalidOperationException("No tool call is admitted."),
            _ => ValueTask.FromResult(ModelTurnReason.None), Token);
        result.Reason.Should().Be(exhausted ? ModelTurnReason.ToolLoopBudgetExceeded : ModelTurnReason.InvalidProviderResponse);
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AdapterExhaustedCumulativeOutputCannotSendAnotherRoundEvenWhenTheLastResultFitsExactly()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var transport = new ToolTransport(VersionCall, FinalAnswer);
        var tool = new CapabilityReply(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "unobserved"));
        transport.ChangeReply = (_, reply) =>
        {
            var call = ((ModelAssistantToolCalls)reply.Message!).Calls[0];
            var bytes = ModelToolProtocol.SerializeMessage(ModelToolProtocol.CreateToolResult(call, tool)).Length;
            return reply with { Usage = reply.Usage! with { OutputUtf8Bytes = ModelToolProtocol.MaximumOutputUtf8Bytes - bytes } };
        };
        await using var loop = CreateLoop(fixture, transport);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), fixture.Request, ModelProviderSelection.OllamaCandidate);
        var result = await loop.RunAsync(provenance, null, fixture.Context().Serialize(),
            (_, _, _) => ValueTask.FromResult(tool), _ => ValueTask.FromResult(ModelTurnReason.None), Token);
        result.Reason.Should().Be(ModelTurnReason.ToolLoopBudgetExceeded);
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AdapterConcurrentEntryCannotBorrowTheRunningTransportAndUnusedDisposalStillOwnsItsTransport()
    {
        using var fixture = new Fixture();
        using var root = fixture.Root();
        var pending = new TaskCompletionSource<ModelToolTransportReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ToolTransport(FinalAnswer) { Pending = pending.Task };
        await using var loop = CreateLoop(fixture, transport);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), fixture.Request, ModelProviderSelection.OllamaCandidate);
        Task<ModelProviderReply> Run() => loop.RunAsync(provenance, null, fixture.Context().Serialize(),
            (_, _, _) => throw new InvalidOperationException("No tool call is admitted."),
            _ => ValueTask.FromResult(ModelTurnReason.None), Token);
        var first = Run();
        try { (await Run()).Reason.Should().Be(ModelTurnReason.ProviderBusy); }
        finally { pending.TrySetResult(ToolTransport.Reply(transport.Requests.Single(), FinalAnswer)); }
        (await first).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var unusedTransport = new ToolTransport();
        var unused = CreateLoop(fixture, unusedTransport);
        await unused.DisposeAsync();
        unusedTransport.Disposals.Should().Be(1);
    }
}
