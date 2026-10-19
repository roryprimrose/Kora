using System.Buffers.Binary;
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
    public async Task Native_transport_and_producer_engine_use_only_the_admitted_callback_and_continue_with_its_exact_result()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message, fixture.Bodies.Count == 1 ? Call : Final));
        await using var adapter = new BoundedModelTurnAdapter(fixture.Transport, fixture.Time,
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
                return ValueTask.FromResult(registry.Invoke(registry.Admit(CapabilityLane.Execution), id, input, token));
            }, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        reply.Response!.Answer.Should().Be("Genuine answer");
        invocations.Should().Equal(ReadOnlyCapabilityCatalog.Version);
        application.Reads.Should().Be(1);
        fixture.Bodies.Should().HaveCount(2);
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
}
