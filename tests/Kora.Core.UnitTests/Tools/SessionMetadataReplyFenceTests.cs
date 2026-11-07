using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Tools;

public sealed class SessionMetadataReplyFenceTests
{
    [Fact]
    public void Metadata_is_rejected_by_every_old_protocol_tool_and_fixed_profile()
    {
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "old", SessionMetadata: SessionMetadataTests.Snapshot());
        foreach (var descriptor in Kora.Core.Mcp.KoraMcpCatalog.Descriptors)
        {
            var call = new ModelToolCall("old_1", descriptor.Id, "{}");
            Action create = () => { _ = ModelToolProtocol.CreateToolResult(call, reply); };
            create.Should().Throw<InvalidDataException>();
        }
        foreach (var profile in Enum.GetValues<SessionQueueReadProfile>())
        {
            if (profile == SessionQueueReadProfile.Capability) { continue; }
            SessionQueueReadProfiles.Accepts(profile, reply).Should().BeFalse();
        }
        CapabilityReplyWire.IsSingleDescriptor(reply, ReadOnlyCapabilityCatalog.Descriptors[0]).Should().BeFalse();
        CapabilityReplyWire.IsSingleRecordedRuntime(reply).Should().BeFalse();
        SessionDiscoveryTool.Matches(reply, new(0, 1)).Should().BeFalse();
    }

    [Fact]
    public void New_result_rejects_old_shapes_and_cross_subject_results()
    {
        var valid = new CapabilityReply(CapabilityOutcome.Succeeded, SessionMetadataTool.ResultReason,
            SessionMetadata: SessionMetadataTests.Snapshot());
        var call = new ModelToolCall("get_1", SessionMetadataTool.Id,
            $$"""{"id":"{{SessionMetadataTests.Id}}"}""");
        CapabilityReply[] mixed =
        [
            valid with { Capabilities = new([], 0, null) }, valid with { Descriptor = ReadOnlyCapabilityCatalog.Descriptors[0] },
            valid with { Version = new("1", "observed") }, valid with { Readiness = new([], 0, null) },
            valid with { Runtimes = new([], 0, null) },
            valid with { Runtime = new("local.inference", RuntimeLocality.Local, CapabilityAvailability.NotObserved,
                null, "unknown", null, false) },
            valid with { WebPage = new(HostRequest.Create(RequestOrigin.LocalUi), new("https://example.invalid"),
                new("https://example.invalid"), DateTimeOffset.UnixEpoch, "inert", false, "text/plain") },
            valid with { Mcp = new("inert", new string('a', 64), DateTimeOffset.UnixEpoch, "inert", null) },
            valid with { Sessions = new([], 0, null) }, valid with { Customizations = new([], 0, null) },
            valid with { Settings = new([], 0, null) },
            valid with { SessionMetrics = new(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, 0, 0, 0, 0, []) },
            valid with { ProviderSettings = new() },
            valid with { Reason = "wrong" }, valid with { SessionMetadata = null },
            valid with { SessionMetadata = valid.SessionMetadata! with { SessionId = "10000000-0000-0000-0000-000000000002" } },
        ];
        foreach (var reply in mixed)
        {
            Action create = () => { _ = ModelToolProtocol.CreateToolResult(call, reply); };
            create.Should().Throw<InvalidDataException>();
        }
        SessionMetadataTool.Matches(valid with { Outcome = CapabilityOutcome.Denied }, SessionMetadataTests.Id).Should().BeFalse();
    }
}
