using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Dependencies;

public sealed partial class ModelToolProtocolTests
{
    [Fact]
    public void ConstraintsRoundTripAndAdvertiseOnlyExactSubsetWithHostBoundTransportBudgets()
    {
        var provenance = Provenance();
        var constraints = new ModelTurnConstraints(1, [ReadOnlyCapabilityCatalog.Version], 1, 2, 16000, 2000, 1000);
        var envelope = new ModelContextEnvelope(provenance.Request, "policy", "input", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), ModelToolMode.ReadOnly, constraints);
        envelope.RequireCompleteInputLimit();
        var context = ModelToolProtocol.ReadContext(envelope.Serialize(), provenance);
        context.Tools.Select(item => item.Id).Should().Equal(ReadOnlyCapabilityCatalog.Version);
        context.Constraints!.Version.Should().Be(1);
        context.Constraints.ToolIds.Should().Equal(ReadOnlyCapabilityCatalog.Version);
        context.Constraints.DeadlineMilliseconds.Should().Be(1000);
        context.Constraints.MaximumCalls.Should().Be(1);
        context.Constraints.MaximumRounds.Should().Be(2);
        var request = ModelToolProtocol.CreateRequest(provenance, null, context, [], 1);
        request.Budget.InputUtf8Bytes.Should().Be(16000);
        request.Budget.OutputUtf8Bytes.Should().Be(2000);
        Action bigger = () => ModelToolProtocol.CreateRequest(provenance, null, context, [], 1, new(16001, 2000));
        bigger.Should().Throw<InvalidDataException>();
        var assistant = Calls(Tool("first", ReadOnlyCapabilityCatalog.List));
        var result = ModelToolProtocol.CreateToolResult(assistant.Calls[0], new(CapabilityOutcome.Succeeded,
            "admitted-read-only", Capabilities: new(ReadOnlyCapabilityCatalog.Descriptors.ToArray(), 6, null)));
        Action escalation = () => ModelToolProtocol.CreateRequest(provenance, null, context, [assistant, result], 2);
        escalation.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("version")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("calls-negative")]
    [InlineData("calls-high")]
    [InlineData("rounds-zero")]
    [InlineData("rounds-high")]
    [InlineData("input-zero")]
    [InlineData("input-high")]
    [InlineData("output-zero")]
    [InlineData("output-high")]
    [InlineData("deadline-zero")]
    [InlineData("deadline-high")]
    [InlineData("empty")]
    [InlineData("default")]
    public void ConstraintsRejectInvalidBoundsAndUnsupportedTools(string fault)
    {
        ImmutableArray<string> ids = fault switch
        {
            "default" => default,
            "empty" => [],
            "duplicate" => [ReadOnlyCapabilityCatalog.Version, ReadOnlyCapabilityCatalog.Version],
            "unknown" => ["agent.run"],
            _ => [ReadOnlyCapabilityCatalog.Version],
        };
        Action act = () => _ = new ModelTurnConstraints(fault is "version" ? 2 : 1, ids,
            fault is "calls-negative" ? -1 : fault is "calls-high" ? 7 : 1,
            fault is "rounds-zero" ? 0 : fault is "rounds-high" ? 3 : 2,
            fault is "input-zero" ? 0 : fault is "input-high" ? 32769 : 16000,
            fault is "output-zero" ? 0 : fault is "output-high" ? 4097 : 2000,
            fault is "deadline-zero" ? 0 : fault is "deadline-high" ? 15001 : 1000);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ConstraintsRejectModeAndWireCatalogueSubstitutionAndCompleteInputOverflow()
    {
        var provenance = Provenance();
        var constraints = new ModelTurnConstraints(1, [], 0, 1, 100, 100, 100);
        Action mode = () => constraints.GetTools(ModelToolMode.LocalWebPage);
        mode.Should().Throw<InvalidDataException>();
        var envelope = new ModelContextEnvelope(provenance.Request, "policy", "input", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(1), ModelToolMode.None, constraints);
        Action overflow = () => envelope.RequireCompleteInputLimit();
        overflow.Should().Throw<InvalidDataException>();
        var wire = JsonNode.Parse(envelope.Serialize())!;
        wire["Tools"] = JsonNode.Parse("[{}]");
        Action substitution = () => ModelToolProtocol.ReadContext(Encoding.UTF8.GetBytes(wire.ToJsonString()), provenance);
        substitution.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ConstraintsPermitCompleteAllowedHistoryButRejectExcessCallsAndRounds()
    {
        var provenance = Provenance();
        var constraints = new ModelTurnConstraints(1, [ReadOnlyCapabilityCatalog.Version], 1, 2, 16000, 2000, 1000);
        ModelToolContext Context(ModelTurnConstraints limits) => ModelToolProtocol.ReadContext(
            new ModelContextEnvelope(provenance.Request, "policy", "input", [], DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddSeconds(1), ModelToolMode.ReadOnly, limits).Serialize(), provenance);
        var assistant = Calls(Tool());
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "Observed"));
        var result = ModelToolProtocol.CreateToolResult(assistant.Calls[0], reply);
        ModelToolProtocol.CreateRequest(provenance, null, Context(constraints), [assistant, result], 2).Round.Should().Be(2);
        var batch = Calls(Tool("one"), Tool("two"));
        var results = batch.Calls.Select(call => ModelToolProtocol.CreateToolResult(call, reply)).ToArray();
        Action excessiveCalls = () => ModelToolProtocol.CreateRequest(provenance, null, Context(constraints),
            [batch, results[0], results[1]], 2);
        excessiveCalls.Should().Throw<InvalidDataException>();
        var oneRound = new ModelTurnConstraints(1, [ReadOnlyCapabilityCatalog.Version], 1, 1, 16000, 2000, 1000);
        Action excessiveRounds = () => ModelToolProtocol.CreateRequest(provenance, null, Context(oneRound), [assistant, result], 2);
        excessiveRounds.Should().Throw<InvalidDataException>();
        Action excessiveCatalogue = () => _ = new ModelTurnConstraints(1,
            Enumerable.Repeat(ReadOnlyCapabilityCatalog.Version, 7).ToImmutableArray(), 1, 1, 16000, 2000, 1000);
        excessiveCatalogue.Should().Throw<InvalidDataException>();
    }
}
