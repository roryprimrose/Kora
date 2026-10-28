using System.Text;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ClaudeModelTurnAdapterTests
{
    [Theory]
    [InlineData("tool-free-runtime", ModelTurnOutcome.Unavailable)]
    [InlineData("missing-account", ModelTurnOutcome.Unavailable)]
    [InlineData("success", ModelTurnOutcome.Succeeded)]
    [InlineData("revoked-after-open", ModelTurnOutcome.Denied)]
    [InlineData("revoked-on-dispose", ModelTurnOutcome.DeniedEgress)]
    public async Task ObservationModeUsesOnlyCanonicalOwnedLoopAndActualAdmittedCallback(string fault, ModelTurnOutcome expected)
    {
        var f = new Fixture(observations: true);
        using var root = f.Root();
        var live = true;
        var transport = new ObservationTransport { OnDispose = () => { if (fault is "revoked-on-dispose") { live = false; } } };
        var runtime = new ObservationRuntime
        {
            Transport = fault is "missing-account" ? null : transport,
            AfterOpen = () => { if (fault is "revoked-after-open") { live = false; } },
        };
        var adapter = new ClaudeModelTurnAdapter(fault is "tool-free-runtime" ? new Runtime() : runtime,
            f.Reviews, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance)
            .Bind(f.Reviews.PrepareClaudeTurn(f.Context, f.Profile), _ => ValueTask.FromResult(live));
        var calls = 0;
        var result = await adapter.RunAsync(f.Provenance, f.Profile, f.Context.Serialize(), (id, input, _) =>
        {
            id.Should().Be(ReadOnlyCapabilityCatalog.Version); input.Should().Be("{}");
            calls++;
            return ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Succeeded, "observed",
                Version: new("1.0", "Not observed by the current version provider.")));
        }, Token);
        result.Outcome.Should().Be(expected);
        (result.Response?.Answer).Should().Be(fault is "success" ? "genuine observation answer" : null);
        calls.Should().Be(fault is "success" or "revoked-on-dispose" ? 1 : 0);
        transport.Requests.Should().HaveCount(fault is "success" or "revoked-on-dispose" ? 2 : 0);
        transport.Disposed.Should().Be(fault is "success" or "revoked-after-open" or "revoked-on-dispose");
        adapter.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task ObservationTransportFailureIsNotConvertedToSuccessAndAlwaysJoinsOwnedDisposal()
    {
        var f = new Fixture(observations: true);
        using var root = f.Root();
        var transport = new ObservationTransport { Fail = true };
        var adapter = new ClaudeModelTurnAdapter(new ObservationRuntime { Transport = transport },
            f.Reviews, f.Time, NullLogger<BoundedModelTurnAdapter>.Instance)
            .Bind(f.Reviews.PrepareClaudeTurn(f.Context, f.Profile), _ => ValueTask.FromResult(true));
        await Assert.ThrowsAsync<IOException>(() => adapter.RunAsync(f.Provenance, f.Profile, f.Context.Serialize(),
            (_, _, _) => throw new InvalidOperationException("Failed transport cannot dispatch."), Token));
        transport.Disposed.Should().BeTrue();
        transport.Requests.Should().ContainSingle();
    }

    private sealed class ObservationRuntime : IClaudeToolTurnRuntime
    {
        internal IModelToolTransport? Transport { get; init; }
        internal Action? AfterOpen { get; init; }
        public bool IsQuiescent => true;
        public Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, Kora.Core.Providers.ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token) =>
            throw new InvalidOperationException("Observation mode must not use the tool-free runtime.");
        public Task<IModelToolTransport?> OpenToolTurnAsync(ModelTurnProvenance provenance,
            Kora.Core.Providers.ProviderProfileTuple profile, ModelContextEnvelope context,
            Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
        {
            AfterOpen?.Invoke();
            return Task.FromResult(Transport);
        }
    }

    private sealed class ObservationTransport : IModelToolTransport
    {
        internal List<ModelToolRequest> Requests { get; } = [];
        internal bool Disposed { get; private set; }
        internal bool Fail { get; init; }
        internal Action? OnDispose { get; init; }
        public Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Fail) { throw new IOException("Synthetic transport failure."); }
            var message = ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(request.Round == 1
                ? """{"kind":"toolCalls","calls":[{"callId":"version_1","capabilityId":"application.get_version","input":{}}]}"""
                : """{"kind":"answer","answer":"genuine observation answer"}"""));
            if (request.Round == 2)
            {
                var result = (ModelToolResultMessage)request.Messages[1];
                result.CallId.Should().Be("version_1");
                Encoding.UTF8.GetString(result.Result.AsSpan()).Should().Contain("1.0");
            }
            return Task.FromResult(new ModelToolTransportReply(ModelTurnOutcome.Succeeded, request.Provenance,
                message, new(ModelToolProtocol.SerializeRequest(request).Length, ModelToolProtocol.SerializeMessage(message).Length)));
        }
        public ValueTask DisposeAsync() { Disposed = true; OnDispose?.Invoke(); return ValueTask.CompletedTask; }
    }
}
