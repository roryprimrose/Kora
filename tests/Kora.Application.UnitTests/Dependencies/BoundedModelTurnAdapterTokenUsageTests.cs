using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

[Collection("Host tracing")]
public sealed class BoundedModelTurnAdapterTokenUsageTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public BoundedModelTurnAdapterTokenUsageTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TotalsSpanModelCallsButContextAndCapacityDescribeOnlyTheLatestRequest()
    {
        var transport = new Transport(new(10, 2, 10, 100), new(30, 4, 30, 200));
        var result = await Run(transport);
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.TokenUsage.Should().Be(new ModelTokenUsage(40, 6, 30, 200));
        transport.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnUnreportedRoundNeverBecomesAKnownPartialTotal(bool missingFirst)
    {
        var result = await Run(new Transport(missingFirst ? null : new(10, 2, 10, 100),
            missingFirst ? new(30, 4, 30, 200) : null));
        result.TokenUsage.Should().Be(new ModelTokenUsage(null, null, missingFirst ? 30 : null, missingFirst ? 200 : null));
    }

    [Fact]
    public async Task MissingIndividualCountsInvalidateOnlyThatTotalAndDoNotReuseOldContext()
    {
        var result = await Run(new Transport(new(10, null, 10, 100), new(null, 4)));
        result.TokenUsage.Should().Be(new ModelTokenUsage());
    }

    [Fact]
    public async Task LegacyByteChargesDoNotSupplyTokens()
    {
        var result = await Run(new Transport(null, null));
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.TokenUsage.Should().BeNull();
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.Unknown)]
    public async Task FailureRepliesRetainReportedUsageAcrossEarlierCalls(ModelTurnOutcome outcome)
    {
        var result = await Run(new Transport(new(10, 2, 10), new(30, 4, 30)) { Outcome = outcome });
        result.Outcome.Should().Be(outcome);
        result.Response.Should().BeNull();
        result.TokenUsage.Should().Be(new ModelTokenUsage(40, 6, 30));
    }

    [Fact]
    public async Task CancellationAfterAReportedCallPreservesCountsWithoutReleasingOwnershipEarly()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var result = await Run(new Transport(new(10, 2, 10), new(30, 4, 30)),
            cancellation, cancelInTool: true);
        result.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        result.TokenUsage.Should().Be(new ModelTokenUsage(10, 2, 10));
    }

    [Fact]
    public async Task CancellationDuringAnUnreportedCallCannotClaimEarlierCountsAreTheTurnTotal()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var result = await Run(new Transport(new(10, 2, 10), null)
        {
            BeforeReply = round =>
            {
                if (round == 2) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            },
        }, cancellation);
        result.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        result.TokenUsage.Should().Be(new ModelTokenUsage());
    }

    [Fact]
    public async Task CancellationRequestedByAReportingTransportKeepsTheJustReportedRound()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var result = await Run(new Transport(new(10, 2, 10), new(30, 4, 30))
        {
            BeforeReply = round =>
            {
                if (round == 2) { cancellation.Cancel(); }
            },
        }, cancellation);
        result.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        result.TokenUsage.Should().Be(new ModelTokenUsage(40, 6, 30));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrOverflowingReportsAreExplicitInvalidProviderResponses(bool overflow)
    {
        var result = await Run(new Transport(new(overflow ? long.MaxValue : 10, 2, 10),
            new(overflow ? 1 : -1, 4, 30)));
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        result.Response.Should().BeNull();
        result.TokenUsage.Should().Be(new ModelTokenUsage());
    }

    [Fact]
    public async Task InvalidFirstReportDoesNotPublishUnvalidatedCounts()
    {
        var result = await Run(new Transport(new ModelTokenUsage(-1, 2, 10)));
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        result.TokenUsage.Should().BeNull();
    }

    [Fact]
    public async Task OutputTotalsCannotOverflowEvenWhenInputTotalsFit()
    {
        var result = await Run(new Transport(new(10, long.MaxValue, 10), new(30, 1, 30)));
        result.Outcome.Should().Be(ModelTurnOutcome.Unknown);
        result.Reason.Should().Be(ModelTurnReason.InvalidProviderResponse);
        result.TokenUsage.Should().Be(new ModelTokenUsage());
    }

    [Fact]
    public async Task ContinuationDenialAfterAReportedCallSuppressesAnswerButNotItsUsage()
    {
        var transport = new Transport(new ModelTokenUsage(10, 2, 10));
        var result = await Run(transport, denyAfterReply: true);
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Response.Should().BeNull();
        result.TokenUsage.Should().Be(new ModelTokenUsage(10, 2, 10));
    }

    private static async Task<ModelProviderReply> Run(Transport transport, CancellationTokenSource? cancellation = null,
        bool cancelInTool = false, bool denyAfterReply = false)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var now = DateTimeOffset.UtcNow;
        var context = new ModelContextEnvelope(request, "policy", "request", [], now, now.AddMinutes(1));
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        await using var adapter = new BoundedModelTurnAdapter(transport, TimeProvider.System,
            NullLogger<BoundedModelTurnAdapter>.Instance);
        var result = await adapter.RunAsync(provenance, null, context.Serialize(), (_, _, _) =>
        {
            if (cancelInTool) { cancellation!.Cancel(); }
            return ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Succeeded, "observed",
                Version: new("1", "not observed")));
        }, _ => ValueTask.FromResult(denyAfterReply && transport.Calls != 0
            ? ModelTurnReason.HostAdmissionClosed : ModelTurnReason.None), cancellation?.Token ?? Token);
        adapter.IsQuiescent.Should().BeTrue();
        return result;
    }

    private sealed class Transport(params ModelTokenUsage?[] usage) : IModelToolTransport
    {
        internal int Calls { get; private set; }
        internal ModelTurnOutcome Outcome { get; init; } = ModelTurnOutcome.Succeeded;
        internal Action<int>? BeforeReply { get; init; }

        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            BeforeReply?.Invoke(Calls);
            var terminal = Calls == usage.Length;
            var outcome = terminal ? Outcome : ModelTurnOutcome.Succeeded;
            var message = outcome == ModelTurnOutcome.Succeeded ? ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(terminal
                ? """{"kind":"answer","answer":"answer"}"""
                : """{"kind":"toolCalls","calls":[{"callId":"one","capabilityId":"application.get_version","input":{}}]}""")) : null;
            return Task.FromResult(new ModelToolTransportReply(outcome, request.Provenance, message,
                message is null ? null : new(ModelToolProtocol.SerializeRequest(request).Length,
                    ModelToolProtocol.SerializeMessage(message).Length)) { TokenUsage = usage[Calls - 1] });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
