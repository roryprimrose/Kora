using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ClaudeModelTurnAdapterTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("missing-publish")]
    [InlineData("missing-runtime")]
    [InlineData("revoked")]
    [InlineData("continuation")]
    [InlineData("foreign-update")]
    public async Task ExplicitStreamAuthorizationRequiresTheStreamingRuntimeAndFreshPublicationAdmission(string fault)
    {
        var f = new Fixture();
        using var root = f.Root();
        var runtime = new AnswerRuntime();
        var live = true;
        var adapter = new ClaudeModelTurnAdapter(fault is "missing-runtime" ? new Runtime() : runtime,
            f.Reviews, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance)
            .Bind(f.Reviews.PrepareClaudeTurn(f.Context, f.Profile, streamAnswer: true), _ => ValueTask.FromResult(live));
        adapter.StreamsAnswer.Should().BeTrue();
        runtime.Publish = (publish, token) =>
        {
            if (fault is "revoked") { live = false; }
            return publish(new(fault is "foreign-update" ? f.Provenance with { Turn = new(Guid.NewGuid()) } : f.Provenance,
                "decoded answer"), token);
        };
        var updates = new List<ModelAnswerUpdate>();
        Task<ModelProviderReply> Run() => fault is "missing-publish"
            ? adapter.RunAsync(f.Provenance, f.Profile, f.Context.Serialize(), NoTools, Token)
            : adapter.RunWithAnswerAsync(f.Provenance, f.Profile, f.Context.Serialize(), NoTools,
                _ => ValueTask.FromResult(fault is "continuation" ? ModelTurnReason.HostAdmissionClosed : ModelTurnReason.None),
                (update, _) => { updates.Add(update); return ValueTask.CompletedTask; }, Token);
        if (fault is "revoked" or "continuation" or "foreign-update")
        { await Assert.ThrowsAsync<InvalidDataException>(Run); }
        else
        {
            var result = await Run();
            result.Outcome.Should().Be(fault is "success" ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unavailable);
            result.Response?.Answer.Should().Be(fault is "success" ? "decoded answer" : null);
        }
        runtime.Buffered.Should().Be(0);
        updates.Count.Should().Be(fault is "success" ? 1 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnboundAnswerCallbackCannotAcquireStreamingAuthorityOrChangeBufferedClaude(bool bound)
    {
        var f = new Fixture();
        using var root = f.Root();
        var runtime = new AnswerRuntime();
        var adapter = new ClaudeModelTurnAdapter(runtime, f.Reviews, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance);
        adapter.StreamsAnswer.Should().BeFalse();
        if (bound) { adapter = adapter.Bind(f.Reviews.PrepareClaudeTurn(f.Context, f.Profile), _ => ValueTask.FromResult(true)); }
        var result = await adapter.RunWithAnswerAsync(f.Provenance, f.Profile, f.Context.Serialize(), NoTools,
            _ => throw new InvalidOperationException("No streaming continuation allowed."),
            (_, _) => throw new InvalidOperationException("Buffered authorization cannot publish."), Token);
        result.Outcome.Should().Be(bound ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        runtime.Buffered.Should().Be(bound ? 1 : 0);
        runtime.Streamed.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamPreparationRejectsToolsAndRetainsImmutableMode(bool observations)
    {
        var f = new Fixture(observations);
        using var root = f.Root();
        if (observations)
        { Assert.Throws<InvalidOperationException>(() => f.Reviews.PrepareClaudeTurn(f.Context, f.Profile, streamAnswer: true)); }
        else { f.Reviews.PrepareClaudeTurn(f.Context, f.Profile, streamAnswer: true).StreamAnswer.Should().BeTrue(); }
    }

    private static ValueTask<CapabilityReply> NoTools(string id, string input, CancellationToken token) =>
        throw new InvalidOperationException("No stream tool callback may execute.");

    private sealed class AnswerRuntime : IClaudeAnswerTurnRuntime
    {
        public bool IsQuiescent => true;
        internal int Buffered { get; private set; }
        internal int Streamed { get; private set; }
        internal Func<Func<ModelAnswerUpdate, CancellationToken, ValueTask>, CancellationToken, ValueTask>? Publish { get; set; }
        public Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
        { Buffered++; return Task.FromResult(new ClaudeTurnReply(ModelTurnOutcome.Succeeded, "buffered")); }
        public async Task<ClaudeTurnReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current,
            Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken token)
        {
            Streamed++;
            if (Publish is not null) { await Publish(publish, token); }
            return new(ModelTurnOutcome.Succeeded, "decoded answer");
        }
    }
}
