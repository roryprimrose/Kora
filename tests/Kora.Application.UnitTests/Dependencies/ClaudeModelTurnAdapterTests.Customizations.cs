using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ClaudeModelTurnAdapterTests
{
    [Fact]
    public async Task SelectedCopyCannotBorrowAnAnswerStreamOperationOrFallBackToBufferedExecution()
    {
        var f = new Fixture();
        using var root = f.Root();
        var runtime = new CopyRuntime();
        var copy = new SelectedHostedDefinition(CustomizationKind.Skill, "source", "revision", "selected", "Description", "Source");
        var adapter = new ClaudeModelTurnAdapter(runtime, f.Reviews, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance)
            .Bind(f.Reviews.PrepareClaudeTurn(f.Context, f.Profile, selection: copy,
                sourceCurrent: _ => ValueTask.FromResult(true)), _ => ValueTask.FromResult(true));
        var result = await adapter.RunWithAnswerAsync(f.Provenance, f.Profile, f.Context.Serialize(),
            (_, _, _) => throw new InvalidOperationException("No selected tools."),
            _ => ValueTask.FromResult(ModelTurnReason.None),
            (_, _) => throw new InvalidOperationException("No selected provisional publication."), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        result.Reason.Should().Be(ModelTurnReason.ToolUseNotPermitted);
        result.Response.Should().BeNull();
        runtime.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("source-before")]
    [InlineData("source-review")]
    [InlineData("source-after")]
    [InlineData("host-after-source")]
    [InlineData("non-capable")]
    public async Task SelectedCopyIsExactAndCannotFallBackOrOutliveSourceAuthority(string fault)
    {
        var f = new Fixture();
        using var root = f.Root();
        var copy = new SelectedHostedDefinition(CustomizationKind.Agent, "source", "revision", "selected",
            "Description", "Complete lower-trust source");
        var source = fault is not "source-before";
        ValueTask<bool> Source(CancellationToken token)
        {
            if (fault is "host-after-source") { f.Time.Now = f.Context.ExpiresAt; }
            return ValueTask.FromResult(source);
        }
        var selected = new CopyRuntime();
        var ordinary = new Runtime();
        var adapter = new ClaudeModelTurnAdapter(fault is "non-capable" ? ordinary : selected,
            f.Reviews, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance)
            .Bind(f.Reviews.PrepareClaudeTurn(f.Context, f.Profile, selection: copy, sourceCurrent: Source),
                _ => ValueTask.FromResult(true));
        selected.Run = async (provenance, profile, context, selection, admit, current, token) =>
        {
            selection.Should().BeSameAs(copy);
            context.Should().BeSameAs(f.Context);
            if (!await current(token)) { return new(ModelTurnOutcome.DeniedEgress); }
            if (fault is "source-review") { source = false; }
            if (!await admit(new(provenance, profile,
                new("https://api.anthropic.com/v1/messages"), new byte[] { 123, 125 }, []), token))
            { return new(ModelTurnOutcome.DeniedEgress); }
            if (fault is "source-after") { source = false; }
            return new(ModelTurnOutcome.Succeeded, "Genuine answer");
        };
        var result = await adapter.RunAsync(f.Provenance, f.Profile, f.Context.Serialize(),
            (_, _, _) => throw new InvalidOperationException("Selected copy cannot invoke tools."), Token);
        result.Outcome.Should().Be(fault is "complete" ? ModelTurnOutcome.Succeeded
            : fault is "non-capable" ? ModelTurnOutcome.Unavailable : ModelTurnOutcome.DeniedEgress);
        result.Response?.Answer.Should().Be(fault is "complete" ? "Genuine answer" : null);
        ordinary.Calls.Should().Be(0);
        selected.Calls.Should().Be(fault is "source-before" or "host-after-source" or "non-capable" ? 0 : 1);
    }

    private sealed class CopyRuntime : IClaudeCustomizationTurnRuntime
    {
        public bool IsQuiescent => true;
        internal int Calls { get; private set; }
        internal Func<ModelTurnProvenance, ProviderProfileTuple, ModelContextEnvelope, SelectedHostedDefinition,
            Func<ClaudeTransmission, CancellationToken, ValueTask<bool>>, Func<CancellationToken, ValueTask<bool>>,
            CancellationToken, Task<ClaudeTurnReply>> Run { get; set; } = null!;
        public Task<ClaudeTurnReply> RunSelectedAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, SelectedHostedDefinition selection,
            Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
        { Calls++; return Run(provenance, profile, context, selection, admit, current, token); }
        public Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token) =>
            throw new InvalidOperationException("Selected data must not fall back to ordinary Claude.");
    }
}
