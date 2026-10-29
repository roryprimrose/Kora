using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class ClaudeObservationToolsTests
{
    [Theory]
    [InlineData(ReadOnlyCapabilityCatalog.List)]
    [InlineData(ReadOnlyCapabilityCatalog.Get)]
    [InlineData(ReadOnlyCapabilityCatalog.Version)]
    [InlineData(ReadOnlyCapabilityCatalog.Readiness)]
    [InlineData(ReadOnlyCapabilityCatalog.RuntimeList)]
    [InlineData(ReadOnlyCapabilityCatalog.RuntimeStatus)]
    public void ExactSchemasMatchCanonicalInputPolicyAndAdvertiseNoExtraFields(string id)
    {
        using var schema = JsonDocument.Parse(ReadOnlyCapabilitySchema.Serialize(id));
        var root = schema.RootElement;
        root.GetProperty("type").GetString().Should().Be("object");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        var descriptor = ReadOnlyCapabilityCatalog.FindExact(id)!;
        var properties = root.GetProperty("properties");
        if (descriptor.Input == CapabilityInputShape.Page)
        {
            properties.EnumerateObject().Select(item => item.Name).Should().Equal("offset", "count");
            properties.GetProperty("count").GetProperty("maximum").GetInt32().Should().Be(6);
            properties.GetProperty("offset").GetProperty("maximum").GetInt32().Should().Be(int.MaxValue);
            root.GetProperty("required").GetArrayLength().Should().Be(0);
            CapabilityInputPolicy.Read(descriptor, """{"offset":0,"count":6}""").DenialReason.Should().BeNull();
        }
        else if (descriptor.Input == CapabilityInputShape.Id)
        {
            root.GetProperty("required")[0].GetString().Should().Be("id");
            var values = properties.GetProperty("id").GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray();
            values.Should().Equal(string.Equals(id, ReadOnlyCapabilityCatalog.Get, StringComparison.Ordinal)
                ? ReadOnlyCapabilityCatalog.Descriptors.Select(item => item.Id) : [ReadOnlyCapabilityCatalog.LocalRuntimeId]);
            foreach (var value in values)
            { CapabilityInputPolicy.Read(descriptor, JsonSerializer.Serialize(new { id = value })).DenialReason.Should().BeNull(); }
        }
        else { properties.EnumerateObject().Should().BeEmpty(); root.GetProperty("required").GetArrayLength().Should().Be(0); }
        CapabilityInputPolicy.Read(descriptor, """{"unknown":"no authority"}""").DenialReason.Should().NotBeNull();
    }

    [Fact]
    public void ScopeIsFrozenExactFiniteAndRejectsEmptyDuplicateUnknownOrOversizedSelection()
    {
        var source = new List<string> { ReadOnlyCapabilityCatalog.Version };
        var constraints = ClaudeObservationTools.Constrain(source);
        source.Clear();
        constraints.ToolIds.Should().Equal(ReadOnlyCapabilityCatalog.Version);
        constraints.MaximumCalls.Should().Be(6);
        constraints.MaximumRounds.Should().Be(7);
        constraints.DeadlineMilliseconds.Should().Be(15000);
        constraints.InputUtf8Bytes.Should().Be(32768);
        constraints.OutputUtf8Bytes.Should().Be(4096);
        foreach (var invalid in new string[][] { [], ["unknown"], [ReadOnlyCapabilityCatalog.Version, ReadOnlyCapabilityCatalog.Version],
            Enumerable.Repeat(ReadOnlyCapabilityCatalog.Version, 7).ToArray() })
        { Assert.Throws<InvalidDataException>(() => ClaudeObservationTools.Constrain(invalid)); }
        Assert.Throws<InvalidDataException>(() => ReadOnlyCapabilitySchema.Serialize("network.get_web_page"));
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("bounded", true)]
    [InlineData("clarification", true)]
    [InlineData("unbounded", false)]
    [InlineData("page", false)]
    [InlineData("sessions", false)]
    [InlineData("evidence", false)]
    [InlineData("mcp", false)]
    public void OnlyExplicitOriginalObservationSubsetOrToolFreeContextIsSupported(string kind, bool expected)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var mode = kind switch
        {
            "none" or "evidence" => ModelToolMode.None,
            "clarification" => ModelToolMode.HostedClarification,
            "page" => ModelToolMode.LocalWebPage,
            "sessions" => ModelToolMode.SessionDiscovery,
            "mcp" => ModelToolMode.McpReadOnly,
            _ => ModelToolMode.ReadOnly,
        };
        var context = new ModelContextEnvelope(request, "policy", "body",
            kind is "evidence" ? [new(new(Guid.NewGuid()), request, new(1), "source", ModelEvidenceDisclosure.HostedEligible)] : [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), mode,
            kind is "bounded" ? ClaudeObservationTools.Constrain([ReadOnlyCapabilityCatalog.Version]) : null,
            kind is "mcp" ? new McpModelTool("mcp.test",
                """{"type":"object","additionalProperties":false,"properties":{},"required":[]}""") : null);
        ClaudeObservationTools.Supports(context).Should().Be(expected);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("\"Readable topic\"")]
    [InlineData("12345678-1234-1234-1234-123456789012 1 0")]
    public void ExplicitTypedOrActivatedCommandPinsExactSubsetWithoutChangingBody(string target)
    {
        const string body = "  Original\r\nrequest -- unchanged  ";
        var command = SessionCommand.Parse("Kora, session request review " + target
            + " claude-tools=application.get_version,runtime.get_status -- " + body, "Kora")!;
        command.Operation.Should().Be(SessionCommandOperation.RequestReview);
        command.RequestProvider.Should().Be(SessionRequestReview.ProviderChoice.Claude);
        command.RequestClaudeTools.Should().Equal(ReadOnlyCapabilityCatalog.Version, ReadOnlyCapabilityCatalog.RuntimeStatus);
        command.RequestBody.Should().Be(body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("application.get_version,application.get_version")]
    [InlineData("application.get_version,network.get_web_page")]
    public void InvalidScopeCannotFallBackToToolFreeOrAnotherProvider(string ids)
    {
        foreach (var target in new[] { "active", "12345678-1234-1234-1234-123456789012 1 0" })
        {
            SessionCommand.Parse("session request review " + target + " claude-tools=" + ids + " -- original", "Kora")!
                .Operation.Should().Be(SessionCommandOperation.Invalid);
        }
    }
}
