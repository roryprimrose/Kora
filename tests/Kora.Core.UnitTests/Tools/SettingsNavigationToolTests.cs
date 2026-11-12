using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Tools;

public sealed class SettingsNavigationToolTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("voice speed", true)]
    [InlineData("speech.windows-rate", true)]
    [InlineData("voice\nspeed", false)]
    public void QueryValidationIsBoundedAndNeverAnInstructionOrPreference(string? query, bool valid)
    {
        SettingsNavigationTool.IsValidQuery(query).Should().Be(valid);
        SettingsNavigationTool.IsValidQuery(new string('a', 256)).Should().BeTrue();
        SettingsNavigationTool.IsValidQuery(new string('a', 257)).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"query":"  voice speed  "}""", true)]
    [InlineData("""{"query":""}""", false)]
    [InlineData("""{"query":" \t "}""", false)]
    [InlineData("""{"query":42}""", false)]
    [InlineData("""{"query":null}""", false)]
    [InlineData("""{"id":"voice"}""", false)]
    [InlineData("""{"query":"theme","apply":true}""", false)]
    [InlineData("""{"query":"theme","query":"voice"}""", false)]
    [InlineData("{}" , false)]
    public void ToolInputRequiresExactlyOneSafeQuery(string json, bool valid)
    {
        var parsed = CapabilityInputPolicy.Read(SettingsNavigationTool.Descriptor, json);
        (parsed.DenialReason is null).Should().Be(valid);
        if (valid) { parsed.SettingsQuery.Should().Be("voice speed"); }
        else { parsed.SettingsQuery.Should().BeNull(); }
    }

    [Fact]
    public void PresentationToolIsSeparateFromObservationAndHasAnExactReceiptAndSchema()
    {
        var descriptor = SettingsNavigationTool.Descriptor;
        descriptor.Effect.Should().Be(CapabilityEffect.NativePresentation);
        descriptor.CallerLanes.Should().Equal(CapabilityLane.Execution);
        ReadOnlyCapabilityCatalog.FindExact(descriptor.Id).Should().BeNull();
        ModelToolProtocol.GetTools(ModelToolMode.ReadOnly).Should().NotContain(descriptor);
        ModelToolProtocol.GetTools(ModelToolMode.InteractiveConversation).Should().Contain(descriptor);
        ModelToolProtocol.GetTools(ModelToolMode.None).Should().BeEmpty();
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) { CapabilityInputSchema.Write(writer, descriptor); }
        using var schema = JsonDocument.Parse(buffer.ToArray());
        schema.RootElement.GetProperty("required")[0].GetString().Should().Be("query");
        schema.RootElement.GetProperty("properties").GetProperty("query").GetProperty("maxLength").GetInt32().Should().Be(256);
        var message = (ModelAssistantToolCalls)ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(
            """{"kind":"toolCalls","calls":[{"callId":"view_1","capabilityId":"settings.view","input":{"query":"voice speed"}}]}"""));
        var call = message.Calls.Single();
        var receipt = new CapabilityReply(CapabilityOutcome.Succeeded, SettingsNavigationTool.ResultReason, Descriptor: descriptor);
        SettingsNavigationTool.Matches(receipt).Should().BeTrue();
        ModelToolProtocol.CreateToolResult(call, receipt).CapabilityId.Should().Be(descriptor.Id);
        FluentActions.Invoking(() => ModelToolProtocol.CreateToolResult(new(call.CallId, call.CapabilityId, "{}"), receipt))
            .Should().Throw<InvalidDataException>();
        foreach (var invalid in new[]
        {
            receipt with { Reason = "invented" },
            receipt with { Descriptor = null },
            receipt with { Version = new("1", "extra payload") },
        })
        {
            SettingsNavigationTool.Matches(invalid).Should().BeFalse();
            FluentActions.Invoking(() => ModelToolProtocol.CreateToolResult(call, invalid)).Should().Throw<InvalidDataException>();
        }
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi),
            ModelProviderSelection.OllamaCandidate);
        var envelope = new ModelContextEnvelope(provenance.Request, SettingsNavigationTool.ModelPolicy,
            "Where is voice speed?", [], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1),
            ModelToolMode.InteractiveConversation);
        ModelToolProtocol.ReadContext(envelope.Serialize(), provenance).Tools.Should().Contain(descriptor);
    }
}
