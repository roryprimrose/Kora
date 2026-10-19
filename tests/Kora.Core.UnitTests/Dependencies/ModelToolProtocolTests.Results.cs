using System.Text;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Dependencies;

public sealed partial class ModelToolProtocolTests
{
    [Fact]
    public void Every_allowed_result_has_one_exact_typed_shape_and_authoritative_complete_bytes()
    {
        var runtime = new RuntimeObservation(ReadOnlyCapabilityCatalog.LocalRuntimeId, RuntimeLocality.Local,
            CapabilityAvailability.NotObserved, null, "not-observed", null, false);
        var readiness = new ReadinessObservation("local.inference", null, CapabilityAvailability.NotObserved, "not-observed", null);
        (string Capability, string Input, CapabilityReply Reply)[] samples =
        [
            (ReadOnlyCapabilityCatalog.Version, "{}",
                new(CapabilityOutcome.Succeeded, "observed", Version: new("1", "unobserved"))),
            (ReadOnlyCapabilityCatalog.Get, """{"id":"application.get_version"}""",
                new(CapabilityOutcome.Succeeded, "read", Descriptor: ReadOnlyCapabilityCatalog.FindExact(ReadOnlyCapabilityCatalog.Version))),
            (ReadOnlyCapabilityCatalog.List, "{}",
                new(CapabilityOutcome.Succeeded, "read", Capabilities: new(ReadOnlyCapabilityCatalog.Descriptors.ToArray(), 6, null))),
            (ReadOnlyCapabilityCatalog.List, """{"offset":1,"count":1}""",
                new(CapabilityOutcome.Succeeded, "read", Capabilities: new([ReadOnlyCapabilityCatalog.Descriptors[1]], 6, 2))),
            (ReadOnlyCapabilityCatalog.Readiness, "{}",
                new(CapabilityOutcome.Succeeded, "read", Readiness: new([readiness], 1, null))),
            (ReadOnlyCapabilityCatalog.RuntimeList, "{}",
                new(CapabilityOutcome.Succeeded, "read", Runtimes: new([runtime], 1, null))),
            (ReadOnlyCapabilityCatalog.RuntimeStatus, """{"id":"local.inference"}""",
                new(CapabilityOutcome.Succeeded, "read", Runtime: runtime)),
        ];
        foreach (var sample in samples)
        {
            var call = Calls(Tool(capability: sample.Capability, input: sample.Input)).Calls[0];
            var result = ModelToolProtocol.CreateToolResult(call, sample.Reply);
            result.Result.Should().Equal(CapabilityReplyWire.Serialize(sample.Reply));
            var wire = Encoding.UTF8.GetString(ModelToolProtocol.SerializeMessage(result));
            wire.Should().Contain("\"kind\":\"toolResult\"").And.Contain("\"result\":");
        }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("two-shapes")]
    [InlineData("wrong-shape")]
    [InlineData("empty-version")]
    [InlineData("empty-deployment")]
    [InlineData("unicode")]
    [InlineData("control")]
    [InlineData("overflow")]
    [InlineData("blank-reason")]
    [InlineData("reason-control")]
    [InlineData("undefined")]
    [InlineData("denied")]
    [InlineData("failed")]
    public void Invalid_host_result_cannot_be_laundered_as_genuine_model_context(string fault)
    {
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed", Version: new("1", "unobserved"));
        reply = fault switch
        {
            "empty" => reply with { Version = null },
            "two-shapes" => reply with { Descriptor = ReadOnlyCapabilityCatalog.Descriptors[0] },
            "wrong-shape" => reply with { Version = null, Descriptor = ReadOnlyCapabilityCatalog.Descriptors[0] },
            "empty-version" => reply with { Version = new("", "unobserved") },
            "empty-deployment" => reply with { Version = new("1", "") },
            "unicode" => reply with { Version = new("\ud800", "unobserved") },
            "control" => reply with { Version = new("\u001b", "unobserved") },
            "overflow" => reply with { Version = new(new string('a', 4096), "unobserved") },
            "blank-reason" => reply with { Reason = " " },
            "reason-control" => reply with { Reason = "\u001b" },
            "undefined" => reply with { Outcome = (CapabilityOutcome)999 },
            "denied" => reply with { Outcome = CapabilityOutcome.Denied },
            _ => reply with { Outcome = CapabilityOutcome.Failed },
        };
        var act = () => ModelToolProtocol.CreateToolResult(Calls(Tool()).Calls[0], reply);
        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("page-missing")]
    [InlineData("page-total")]
    [InlineData("page-count")]
    [InlineData("page-next")]
    [InlineData("page-descriptor")]
    [InlineData("descriptor")]
    [InlineData("runtime")]
    [InlineData("runtime-qualified")]
    [InlineData("runtime-enum")]
    [InlineData("runtime-page")]
    [InlineData("readiness")]
    [InlineData("readiness-enum")]
    [InlineData("readiness-state")]
    [InlineData("readiness-control")]
    public void Corrupt_recorded_shapes_enums_pagination_and_qualification_are_rejected(string fault)
    {
        var capability = ReadOnlyCapabilityCatalog.List;
        var input = "{}";
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed",
            Capabilities: new(ReadOnlyCapabilityCatalog.Descriptors.ToArray(), 6, null));
        switch (fault)
        {
            case "page-missing": reply = reply with { Capabilities = new(null!, 6, null) }; break;
            case "page-total": reply = reply with { Capabilities = new([], 0, null) }; break;
            case "page-count": reply = reply with { Capabilities = new([], 6, null) }; break;
            case "page-next": reply = reply with { Capabilities = reply.Capabilities! with { NextOffset = 6 } }; break;
            case "page-descriptor":
                reply = reply with { Capabilities = new(
                    ReadOnlyCapabilityCatalog.Descriptors.Select(item => item with { SchemaVersion = 999 }).ToArray(), 6, null) };
                break;
            case "descriptor":
                capability = ReadOnlyCapabilityCatalog.Get;
                input = """{"id":"application.get_version"}""";
                reply = new(CapabilityOutcome.Succeeded, "read", Descriptor: ReadOnlyCapabilityCatalog.Descriptors[0]);
                break;
            case "runtime":
            case "runtime-qualified":
            case "runtime-enum":
            case "runtime-page":
                var runtime = new RuntimeObservation(ReadOnlyCapabilityCatalog.LocalRuntimeId, RuntimeLocality.Local,
                    CapabilityAvailability.NotObserved, null, "not-observed", null, fault == "runtime-qualified");
                if (fault == "runtime-enum") { runtime = runtime with { Locality = (RuntimeLocality)999 }; }
                capability = fault == "runtime-page" ? ReadOnlyCapabilityCatalog.RuntimeList : ReadOnlyCapabilityCatalog.RuntimeStatus;
                input = fault == "runtime-page" ? "{}" : """{"id":"local.inference"}""";
                reply = fault == "runtime-page"
                    ? new(CapabilityOutcome.Succeeded, "read", Runtimes: new([runtime, runtime], 2, null))
                    : new(CapabilityOutcome.Succeeded, "read", Runtime: fault == "runtime" ? null : runtime);
                break;
            default:
                capability = ReadOnlyCapabilityCatalog.Readiness;
                var readiness = new ReadinessObservation("local.inference", null, CapabilityAvailability.NotObserved,
                    "not-observed", null);
                readiness = fault switch
                {
                    "readiness-enum" => readiness with { Availability = (CapabilityAvailability)999 },
                    "readiness-state" => readiness with { Readiness = (DependencyReadiness)999 },
                    "readiness-control" => readiness with { Reason = "\u0000" },
                    _ => null!,
                };
                reply = new(CapabilityOutcome.Succeeded, "read", Readiness: new([readiness], 1, null));
                break;
        }
        var call = Calls(Tool(capability: capability, input: input)).Calls[0];
        var act = () => ModelToolProtocol.CreateToolResult(call, reply);
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Input_policy_and_protocol_keep_null_and_undefined_contract_failures_explicit()
    {
        var descriptor = ReadOnlyCapabilityCatalog.Descriptors[0];
        CapabilityInputPolicy.Read(descriptor, "{\"offset\":1,\"count\":1}").Page.Should().Be(new CapabilityPageInput(1, 1));
        CapabilityInputPolicy.Read(descriptor with { Input = (CapabilityInputShape)999 }, "{}").DenialReason.Should().Be("invalid-page-input");
        CapabilityInputPolicy.Read(descriptor, "\ud800").DenialReason.Should().Be("invalid-input");
        CapabilityInputPolicy.Read(descriptor, new string('a', 1025)).DenialReason.Should().Be("input-limit-exceeded");
        var nullDescriptor = () => CapabilityInputPolicy.Read(null!, "{}");
        nullDescriptor.Should().Throw<ArgumentNullException>();
        var nullInput = () => CapabilityInputPolicy.Read(descriptor, null!);
        nullInput.Should().Throw<ArgumentNullException>();
        var nullProvenance = () => ModelToolProtocol.ValidateProvenance(null!);
        nullProvenance.Should().Throw<ArgumentNullException>();
        var nullMessage = () => ModelToolProtocol.SerializeMessage(null!);
        nullMessage.Should().Throw<ArgumentNullException>();
        var nullRequest = () => ModelToolProtocol.SerializeRequest(null!);
        nullRequest.Should().Throw<ArgumentNullException>();
        var nullCall = () => ModelToolProtocol.CreateToolResult(null!, new(CapabilityOutcome.Succeeded, "read"));
        nullCall.Should().Throw<ArgumentNullException>();
        var nullReply = () => ModelToolProtocol.CreateToolResult(Calls(Tool()).Calls[0], null!);
        nullReply.Should().Throw<ArgumentNullException>();
    }
}
