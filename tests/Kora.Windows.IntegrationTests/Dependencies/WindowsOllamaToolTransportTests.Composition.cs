using System.Buffers.Binary;
using System.Text;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Tools;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Fact]
    public void DesktopCompositionCannotUseTheLegacyHttpReasonerAsAnEligibilityBypass()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new InvalidOperationException("The repository source root is unavailable.");
        File.ReadAllText(Path.Combine(root, "src", "Kora", "Program.cs"))
            .Should().Contain("services.AddSingleton<ILocalModelReasoner, UnavailableLocalModelReasoner>();")
            .And.NotContain("new WindowsOllamaReasoner(")
            .And.NotContain("\"ollama-reasoner\"");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_transport_and_producer_engine_use_the_live_guard_and_only_the_admitted_registry_callback(
        bool withdrawAfterTool)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message, fixture.Bodies.Count == 1 ? Call : Final));
        var recording = new RecordingTransport(fixture.Transport);
        await using var adapter = new BoundedModelTurnAdapter(recording, fixture.Time,
            NullLogger<BoundedModelTurnAdapter>.Instance);
        var application = new ObservedApplication();
        using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance, fixture.Time);
        var recorded = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
        var registry = new ReadOnlyCapabilityRegistry(application,
            new Kora.Tools.Capabilities.CapabilitiesList(), new Kora.Tools.Capabilities.CapabilitiesGet(),
            new Kora.Tools.Application.ApplicationGetVersion(application), new Kora.Tools.Readiness.ReadinessGet(dependencies),
            new Kora.Tools.Runtime.RuntimeList(recorded), new Kora.Tools.Runtime.RuntimeGetStatus(recorded),
            NullLogger<ReadOnlyCapabilityRegistry>.Instance);
        var invocations = new List<string>();
        var request = fixture.Request();
        var reply = await adapter.RunAsync(fixture.Provenance, fixture.Profile, request.Context.Wire.ToArray(),
            (id, input, token) =>
            {
                token.ThrowIfCancellationRequested();
                invocations.Add(id);
                input.Should().Be("{}");
                var result = registry.Invoke(registry.Admit(CapabilityLane.Execution), id, input, token);
                fixture.Admitted = !withdrawAfterTool;
                return ValueTask.FromResult(result);
            }, token => ValidateContinuation(fixture, token), Token);
        reply.Outcome.Should().Be(withdrawAfterTool ? ModelTurnOutcome.Denied : ModelTurnOutcome.Succeeded);
        invocations.Should().Equal(ReadOnlyCapabilityCatalog.Version);
        application.Reads.Should().Be(1);
        fixture.Bodies.Should().HaveCount(withdrawAfterTool ? 1 : 2);
        if (withdrawAfterTool)
        {
            reply.Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
            reply.Response.Should().BeNull();
        }
        else
        {
            reply.Response!.Answer.Should().Be("Genuine answer");
            var firstUsage = recording.Replies[0].Usage!;
            recording.Requests[1].Budget.InputUtf8Bytes.Should()
                .Be(ModelToolProtocol.MaximumInputUtf8Bytes - firstUsage.InputUtf8Bytes);
            var result = recording.Requests[1].Messages.OfType<ModelToolResultMessage>().Single();
            recording.Requests[1].Budget.OutputUtf8Bytes.Should().Be(ModelToolProtocol.MaximumOutputUtf8Bytes
                - firstUsage.OutputUtf8Bytes - ModelToolProtocol.SerializeMessage(result).Length);
        }
        for (var index = 0; index < recording.Replies.Count; index++)
        {
            var transportReply = recording.Replies[index];
            transportReply.Provenance.Should().BeSameAs(fixture.Provenance);
            transportReply.Usage.Should().NotBeNull();
            var usage = transportReply.Usage!;
            usage.InputUtf8Bytes.Should().Be(Math.Max(fixture.Bodies[index].Length,
                ModelToolProtocol.SerializeRequest(recording.Requests[index]).Length));
            usage.OutputUtf8Bytes.Should().Be(Math.Max(Encoding.UTF8.GetByteCount(index == 0 ? Call : Final),
                ModelToolProtocol.SerializeMessage(transportReply.Message!).Length));
        }
    }

    [Fact]
    public async Task Original_engine_entry_cannot_bypass_the_required_live_host_guard()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        await using var adapter = new BoundedModelTurnAdapter(fixture.Transport, fixture.Time,
            NullLogger<BoundedModelTurnAdapter>.Instance);
        var invocations = 0;
        var reply = await adapter.RunAsync(fixture.Provenance, fixture.Profile, fixture.Request().Context.Wire.ToArray(),
            (_, _, _) => { invocations++; return ValueTask.FromResult(VersionReply()); }, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Reason.Should().Be(ModelTurnReason.HostContextRequired);
        invocations.Should().Be(0);
        fixture.Observations.Should().Be(0);
        fixture.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task Live_guard_withdrawal_before_the_first_exchange_never_reaches_native_observation_or_HTTP()
    {
        await using var fixture = new Fixture { Admitted = false };
        using var root = fixture.Root();
        await using var adapter = new BoundedModelTurnAdapter(fixture.Transport, fixture.Time,
            NullLogger<BoundedModelTurnAdapter>.Instance);
        var invocations = 0;
        var reply = await adapter.RunAsync(fixture.Provenance, fixture.Profile, fixture.Request().Context.Wire.ToArray(),
            (_, _, _) => { invocations++; return ValueTask.FromResult(VersionReply()); },
            token => ValidateContinuation(fixture, token), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Reason.Should().Be(ModelTurnReason.HostAdmissionClosed);
        reply.Response.Should().BeNull();
        invocations.Should().Be(0);
        fixture.Observations.Should().Be(0);
        fixture.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task Composed_native_implementation_does_not_supply_qualification_evidence()
    {
        await using var fixture = new Fixture();
        await using var adapter = new BoundedModelTurnAdapter(fixture.Transport, fixture.Time,
            NullLogger<BoundedModelTurnAdapter>.Instance);
        var registration = new ModelProviderRegistration(ModelProviderSelection.OllamaCandidate,
            ModelQualificationGate.None, DateTimeOffset.MinValue, adapter);
        registration.IsQualified(fixture.Time.GetUtcNow()).Should().BeFalse();
        fixture.Paths.Should().BeEmpty();
    }

    [Theory]
    [InlineData(4242, 0x0100007Fu, true)]
    [InlineData(4243, 0x0100007Fu, false)]
    [InlineData(4242, 0u, false)]
    [InlineData(4242, 0x0200007Fu, false)]
    public void TCP_listener_observation_requires_the_exact_process_and_exact_fixed_loopback_address(
        int observedProcess, uint address, bool expected)
    {
        var bytes = Listener(observedProcess, address);
        WindowsOllamaToolProcessObservation.MatchesListener(bytes, 4242).Should().Be(expected);
    }

    [Fact]
    public void Malformed_or_ambiguous_listener_tables_are_not_endpoint_ownership()
    {
        WindowsOllamaToolProcessObservation.MatchesListener([], 4242).Should().BeFalse();
        var bytes = Listener(4242, 0x0100007F);
        WindowsOllamaToolProcessObservation.MatchesListener(bytes.AsSpan(0, 27), 4242).Should().BeFalse();
        var duplicate = new byte[52];
        BinaryPrimitives.WriteUInt32LittleEndian(duplicate, 2);
        bytes.AsSpan(4).CopyTo(duplicate.AsSpan(4));
        bytes.AsSpan(4).CopyTo(duplicate.AsSpan(28));
        WindowsOllamaToolProcessObservation.MatchesListener(duplicate, 4242).Should().BeFalse();
    }

    private static byte[] Listener(int process, uint address)
    {
        var bytes = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), address);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), 11434);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), process);
        return bytes;
    }

    private sealed class ObservedApplication : IApplicationInfo, ICapabilityHostAccess
    {
        internal int Reads;
        public bool IsCurrentHost => true;
        public string Version { get { Reads++; return "1.2.3"; } }
    }

    private static ValueTask<ModelTurnReason> ValidateContinuation(Fixture fixture, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(fixture.Admitted ? ModelTurnReason.None : ModelTurnReason.HostAdmissionClosed);
    }

    private sealed class RecordingTransport(IModelToolTransport inner) : IModelToolTransport
    {
        internal readonly List<ModelToolRequest> Requests = [];
        internal readonly List<ModelToolTransportReply> Replies = [];
        public async Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var reply = await inner.CompleteAsync(request, cancellationToken);
            Replies.Add(reply);
            return reply;
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
