using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Dependencies;

public sealed partial class ModelToolProtocolTests
{
    [Theory]
    [InlineData(ModelToolMode.ReadOnly, 6)]
    [InlineData(ModelToolMode.None, 0)]
    public void ImmutableHostToolModeSelectsTheExactCatalogueAndSurvivesTheCompleteRequest(
        ModelToolMode mode, int tools)
    {
        var provenance = Provenance();
        var original = Envelope(provenance);
        var envelope = new ModelContextEnvelope(provenance.Request, original.SystemPolicy, original.UserRequest,
            original.Evidence, original.CreatedAt, original.ExpiresAt, toolMode: mode);
        envelope.ToolMode.Should().Be(mode);
        using var wire = JsonDocument.Parse(envelope.Serialize());
        wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)mode);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(tools);
        var context = ModelToolProtocol.ReadContext(envelope.Serialize(), provenance);
        context.ToolMode.Should().Be(mode);
        var request = ModelToolProtocol.CreateRequest(provenance, null, context, [], 1);
        request.Context.ToolMode.Should().Be(mode);
        using var sent = JsonDocument.Parse(ModelToolProtocol.SerializeRequest(request));
        sent.RootElement.GetProperty("context").GetProperty("Tools").GetArrayLength().Should().Be(tools);
    }

    [Fact]
    public void UnknownHostToolModeCannotConstructAnEnvelope()
    {
        var provenance = Provenance();
        var action = () => new ModelContextEnvelope(provenance.Request, "policy", "source", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), (ModelToolMode)999);
        action.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("null")]
    [InlineData("text")]
    [InlineData("empty-readonly")]
    [InlineData("populated-none")]
    public void MissingUnknownOrMismatchedToolModeAndCatalogueNeverDefaultsToToolAuthority(string fault)
    {
        var provenance = Provenance();
        var wire = JsonNode.Parse(Envelope(provenance).Serialize())!.AsObject();
        switch (fault)
        {
            case "missing": wire.Remove("ToolMode"); break;
            case "unknown": wire["ToolMode"] = 999; break;
            case "null": wire["ToolMode"] = null; break;
            case "text": wire["ToolMode"] = "None"; break;
            case "empty-readonly": wire["Tools"] = new JsonArray(); break;
            case "populated-none": wire["ToolMode"] = (int)ModelToolMode.None; break;
        }
        var action = () => ModelToolProtocol.ReadContext(JsonSerializer.SerializeToUtf8Bytes(wire), provenance);
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ToolFreeContextRejectsAnyPriorToolHistoryEvenWhenEveryCallAndResultIsValid()
    {
        var provenance = Provenance();
        var envelope = new ModelContextEnvelope(provenance.Request, "policy", "source", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), ModelToolMode.None);
        var context = ModelToolProtocol.ReadContext(envelope.Serialize(), provenance);
        var calls = Calls(Tool());
        var result = ModelToolProtocol.CreateToolResult(calls.Calls[0],
            new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0", "unobserved")));
        var action = () => ModelToolProtocol.CreateRequest(provenance, null, context, [calls, result], 2);
        action.Should().Throw<InvalidDataException>();
    }
}
