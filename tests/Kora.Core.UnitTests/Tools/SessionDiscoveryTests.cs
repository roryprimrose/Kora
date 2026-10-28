using System.Collections.Immutable;
using System.Text;

using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Tools;

public sealed class SessionDiscoveryTests
{
    private const string SessionId = "10000000-0000-0000-0000-000000000001";
    private static ModelToolCall Call(string input = "{}") => new("page_1", SessionDiscoveryTool.Id, input);
    private static CapabilityReply Reply(SessionDescriptor? record = null) => new(CapabilityOutcome.Succeeded,
        SessionDiscoveryTool.ResultReason, Sessions: new([record ?? new(SessionId, "Name")], 1, null));

    [Fact]
    public void The_exact_source_catalogue_is_separate_and_roundtrips_without_expanding_six_default_capabilities()
    {
        ReadOnlyCapabilityCatalog.Descriptors.Should().HaveCount(6);
        ReadOnlyCapabilityCatalog.FindExact(SessionDiscoveryTool.Id).Should().BeNull();
        ModelToolProtocol.GetTools(ModelToolMode.None).Should().BeEmpty();
        ModelToolProtocol.GetTools(ModelToolMode.ReadOnly).Should().Equal(ReadOnlyCapabilityCatalog.Descriptors);
        ModelToolProtocol.GetTools(ModelToolMode.SessionDiscovery).Should().Equal(SessionDiscoveryTool.Descriptor);
        SessionDiscoveryTool.Descriptor.CallerLanes.Should().Equal(CapabilityLane.Execution);
        SessionDiscoveryTool.Descriptor.Output.Should().Be(CapabilityOutputShape.Sessions);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi),
            ModelProviderSelection.OllamaCandidate);
        var envelope = new ModelContextEnvelope(provenance.Request, "policy", "Explicit context request", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1), ModelToolMode.SessionDiscovery);
        var context = ModelToolProtocol.ReadContext(envelope.Serialize(), provenance);
        var request = ModelToolProtocol.CreateRequest(provenance, null, context, [], 1);
        ModelToolProtocol.SerializeRequest(request).Should().NotBeEmpty();
        var hosted = provenance with { Selection = ModelProviderSelection.CopilotCandidate };
        var hostedContext = ModelToolProtocol.ReadContext(envelope.Serialize(), hosted);
        var sendHosted = () => ModelToolProtocol.CreateRequest(hosted, null, hostedContext, [], 1);
        sendHosted.Should().Throw<InvalidDataException>();
        var assistant = (ModelAssistantToolCalls)ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(
            """{"kind":"toolCalls","calls":[{"callId":"page_1","capabilityId":"sessions.list","input":{}}]}"""));
        var result = ModelToolProtocol.CreateToolResult(assistant.Calls[0], Reply());
        result.Result.Should().Equal(CapabilityReplyWire.Serialize(Reply()));
        ModelToolProtocol.CreateRequest(provenance, null, context, [assistant, result], 2).Messages.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("""{"count":0}""")]
    [InlineData("""{"count":7}""")]
    [InlineData("""{"offset":-1}""")]
    [InlineData("""{"count":1,"count":2}""")]
    [InlineData("""{"sessionId":"foreign"}""")]
    [InlineData("""{"includeHistory":true}""")]
    [InlineData("""{"approved":true}""")]
    [InlineData("""{"cursor":"unreviewed"}""")]
    public void Inputs_cannot_supply_authority_or_unbounded_source_pagination(string input)
    {
        CapabilityInputPolicy.Read(SessionDiscoveryTool.Descriptor, input).DenialReason.Should().NotBeNull();
        var parse = () => ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(
            $$"""{"kind":"toolCalls","calls":[{"callId":"page_1","capabilityId":"sessions.list","input":{{input}}}]}"""));
        parse.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-id")]
    [InlineData("10000000-0000-0000-0000-00000000000A")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Records_require_canonical_nonempty_ids(string? id) =>
        SessionDiscoveryTool.IsValidRecord(id is null ? null : new(id, null)).Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData(" trim")]
    [InlineData("line\nbreak")]
    public void Names_reuse_authoritative_unicode_policy_without_normalizing_or_repairing(string name) =>
        SessionDiscoveryTool.IsValidRecord(new(SessionId, name)).Should().BeFalse();

    [Fact]
    public void Malformed_in_memory_unicode_is_not_test_framework_attribute_replacement_text() =>
        SessionDiscoveryTool.IsValidRecord(new(SessionId, new string((char)0xd800, 1))).Should().BeFalse();

    [Theory]
    [InlineData("reason")]
    [InlineData("missing")]
    [InlineData("null-records")]
    [InlineData("negative-total")]
    [InlineData("large-total")]
    [InlineData("bad-next")]
    [InlineData("record-count")]
    [InlineData("bad-id")]
    [InlineData("duplicates")]
    [InlineData("mixed-version")]
    [InlineData("mixed-runtime")]
    [InlineData("mixed-catalogue")]
    public void Corrupt_results_are_rejected_whole(string corruption)
    {
        var reply = corruption switch
        {
            "reason" => Reply() with { Reason = "authority" },
            "missing" => Reply() with { Sessions = null },
            "null-records" => Reply() with { Sessions = new(null!, 1, null) },
            "negative-total" => Reply() with { Sessions = new([], -1, null) },
            "large-total" => Reply() with { Sessions = new([], 7, null) },
            "bad-next" => Reply() with { Sessions = new([new(SessionId, null)], 1, 1) },
            "record-count" => Reply() with { Sessions = new([], 1, null) },
            "bad-id" => Reply(new("foreign", null)),
            "duplicates" => Reply() with { Sessions = new([new(SessionId, null), new(SessionId, null)], 2, null) },
            "mixed-version" => Reply() with { Version = new("1", "observed") },
            "mixed-runtime" => Reply() with { Runtimes = new([], 0, null) },
            _ => Reply() with { Descriptor = ReadOnlyCapabilityCatalog.Descriptors[0] },
        };
        var result = () => ModelToolProtocol.CreateToolResult(Call(), reply);
        result.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Empty_reviewed_pages_are_truthful_and_metadata_cannot_be_smuggled_through_another_tool()
    {
        ModelToolProtocol.CreateToolResult(Call(), Reply() with { Sessions = new([], 0, null) })
            .Result.Should().NotBeEmpty();
        var invalidOffset = () => ModelToolProtocol.CreateToolResult(Call("""{"offset":1}"""),
            Reply() with { Sessions = new([], 0, null) });
        invalidOffset.Should().Throw<InvalidDataException>();
        var invalidInput = () => ModelToolProtocol.CreateToolResult(Call("""{"approved":true}"""), Reply());
        invalidInput.Should().Throw<InvalidDataException>();
        var mixed = () => ModelToolProtocol.CreateToolResult(new("v", ReadOnlyCapabilityCatalog.Version, "{}"),
            Reply() with { Version = new("1", "observed") });
        mixed.Should().Throw<InvalidDataException>();
        CapabilityReplyWire.IsSingleDescriptor(Reply() with { Descriptor = ReadOnlyCapabilityCatalog.Descriptors[0] },
            ReadOnlyCapabilityCatalog.Descriptors[0]).Should().BeFalse();
    }

    [Fact]
    public void Strict_utf8_input_limit_counts_complete_bytes_not_characters()
    {
        var exact = "{}" + new string(' ', 1022);
        CapabilityInputPolicy.Read(SessionDiscoveryTool.Descriptor, exact).DenialReason.Should().BeNull();
        CapabilityInputPolicy.Read(SessionDiscoveryTool.Descriptor, exact + " ").DenialReason.Should().Be("input-limit-exceeded");
        CapabilityInputPolicy.Read(SessionDiscoveryTool.Descriptor, "{\"x\":\"" + new string('\u00e9', 1024) + "\"}")
            .DenialReason.Should().Be("input-limit-exceeded");
        var noHistory = new ModelTurnConstraints(1, ImmutableArray<string>.Empty, 0, 1, 32768, 4096, 1000);
        var widen = () => noHistory.GetTools(ModelToolMode.SessionDiscovery);
        widen.Should().Throw<InvalidDataException>();
    }
}
