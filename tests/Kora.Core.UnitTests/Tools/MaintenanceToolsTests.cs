using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Maintenance;
using Kora.Core.Mcp;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Tools;

public sealed class MaintenanceToolsTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Theory]
    [InlineData("capabilities")]
    [InlineData("descriptor")]
    [InlineData("version")]
    [InlineData("readiness")]
    [InlineData("runtimes")]
    [InlineData("runtime")]
    [InlineData("webPage")]
    [InlineData("mcp")]
    [InlineData("sessions")]
    [InlineData("customizations")]
    [InlineData("settings")]
    [InlineData("sessionMetrics")]
    [InlineData("providerSettings")]
    [InlineData("sessionMetadata")]
    public void MaintenanceCannotSmuggleAnotherResultShape(string field)
    {
        var reply = Success();
        var json = JsonNode.Parse(CapabilityReplyWire.Serialize(reply))!.AsObject();
        json[field] = new JsonObject();
        var mixed = json.Deserialize<CapabilityReply>(Options)!;
        MaintenanceTools.Matches(mixed, MaintenanceTools.Upgrade).Should().BeFalse();
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("channel")]
    [InlineData("availability")]
    [InlineData("installer")]
    [InlineData("empty")]
    [InlineData("large")]
    [InlineData("current")]
    [InlineData("target")]
    [InlineData("missing-target")]
    public void CorruptMaintenanceObservationsNeverBecomeSuccessfulLaunches(string field)
    {
        var reply = Success();
        var result = reply.Maintenance!;
        reply = field switch
        {
            "reason" => reply with { Reason = "forged" },
            "channel" => reply with { Maintenance = result with { Channel = (ReleaseChannel)99 } },
            "availability" => reply with { Maintenance = result with { Availability = (ReleaseAvailability)99 } },
            "installer" => reply with { Maintenance = result with { Installer = (InstallerStartOutcome)99 } },
            "empty" => reply with { Maintenance = result with { Message = " " } },
            "large" => reply with { Maintenance = result with { Message = new string('x', MaintenanceCommandParser.MaximumOutputLength + 1) } },
            "current" => reply with { Maintenance = result with { CurrentVersion = "invalid" } },
            "target" => reply with { Maintenance = result with { ReleaseVersion = "invalid" } },
            _ => reply with { Maintenance = result with { ReleaseVersion = null } },
        };
        MaintenanceTools.Matches(reply, MaintenanceTools.Upgrade).Should().BeFalse();
    }

    [Fact]
    public void CheckAndDeniedObservationsRemainTruthfulAndVersionOnlyInputsAreBounded()
    {
        var reply = Success();
        MaintenanceTools.Matches(reply with { Maintenance = null }, MaintenanceTools.Upgrade).Should().BeFalse();
        MaintenanceTools.Matches(new(CapabilityOutcome.Denied, "unavailable"), MaintenanceTools.Upgrade).Should().BeTrue();
        MaintenanceTools.Matches(reply with { Outcome = CapabilityOutcome.Denied }, MaintenanceTools.Upgrade).Should().BeTrue();
        MaintenanceTools.Matches(reply, "unknown").Should().BeFalse();
        var check = reply with { Maintenance = reply.Maintenance! with { Installer = null, Availability = ReleaseAvailability.UpToDate } };
        MaintenanceTools.Matches(check, MaintenanceTools.Check).Should().BeTrue();
        MaintenanceTools.Matches(check with { Maintenance = check.Maintenance! with { Availability = ReleaseAvailability.Available } },
            MaintenanceTools.Check).Should().BeTrue();
        MaintenanceTools.Matches(check with { Maintenance = check.Maintenance! with { Availability = ReleaseAvailability.Unknown } },
            MaintenanceTools.Check).Should().BeFalse();
        MaintenanceTools.Matches(reply with { Maintenance = reply.Maintenance! with { Availability = ReleaseAvailability.UpToDate } },
            MaintenanceTools.Upgrade).Should().BeFalse();
        var oversized = JsonSerializer.Serialize(new { version = new string('1', 65) });
        CapabilityInputPolicy.Read(MaintenanceTools.Descriptors[0], oversized).DenialReason.Should().NotBeNull();
    }

    private static CapabilityReply Success() => new(CapabilityOutcome.Succeeded, MaintenanceTools.ResultReason,
        Maintenance: new("1.0.0", ReleaseChannel.Production, ReleaseAvailability.Available,
            "Installer started, not installed.", "1.2.3", InstallerStartOutcome.Started));

    [Fact]
    public void MaintenanceIsBuiltInButNotGrantedByReadOnlyOrToolFreeModes()
    {
        KoraMcpCatalog.ToolIds.Should().Contain(MaintenanceTools.Check).And.Contain(MaintenanceTools.Upgrade);
        ModelToolProtocol.GetTools(ModelToolMode.ApplicationMaintenance).Should().Equal(MaintenanceTools.Descriptors);
        ModelToolProtocol.GetTools(ModelToolMode.ReadOnly).Should().NotContain(item => item.Id == MaintenanceTools.Upgrade);
        ModelToolProtocol.GetTools(ModelToolMode.None).Should().BeEmpty();
        MaintenanceTools.FindExact("application.UPGRADE").Should().BeNull();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":\"1.2.3\"}")]
    [InlineData("{\"version\":\"1.2.3-beta7\"}")]
    public void OnlyCanonicalVersionInputsAreAdmitted(string input)
    {
        var descriptor = MaintenanceTools.FindExact(MaintenanceTools.Upgrade)!;
        CapabilityInputPolicy.Read(descriptor, input).DenialReason.Should().BeNull();
        using var document = JsonDocument.Parse(input);
        var version = MaintenanceTools.ReadVersion(document.RootElement);
        using var roundTrip = JsonDocument.Parse(MaintenanceTools.CreateInput(version));
        var actual = MaintenanceTools.ReadVersion(roundTrip.RootElement);
        if (version is null) { actual.Should().BeNull(); }
        else { actual.Should().Be(version); }
    }

    [Theory]
    [InlineData("{\"version\":null}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":\"https://example.invalid/setup.exe\"}")]
    [InlineData("{\"approved\":true}")]
    [InlineData("{\"path\":\"Q:\\\\setup.exe\"}")]
    [InlineData("{\"version\":\"1.2.3\",\"version\":\"1.2.4\"}")]
    [InlineData("{\"version\":\"1.2.3\",\"approved\":true}")]
    public void ToolInputCannotSupplyExecutionOrApprovalAuthority(string input)
    {
        CapabilityInputPolicy.Read(MaintenanceTools.Descriptors[1], input).DenialReason.Should().NotBeNull();
    }

    [Theory]
    [InlineData("input")]
    [InlineData("version")]
    [InlineData("foreign-tool")]
    [InlineData("denied")]
    public void ModelProtocolRejectsUnboundOrNonSuccessfulMaintenanceResults(string defect)
    {
        var call = new ModelToolCall("upgrade_1", MaintenanceTools.Upgrade, "{}");
        var reply = Success();
        if (defect is "input") { call = new(call.CallId, call.CapabilityId, """{"approved":true}"""); }
        if (defect is "version") { call = new(call.CallId, call.CapabilityId, """{"version":"1.2.4"}"""); }
        if (defect is "foreign-tool") { call = new(call.CallId, ReadOnlyCapabilityCatalog.Version, call.InputJson); }
        if (defect is "denied") { reply = reply with { Outcome = CapabilityOutcome.Denied }; }

        FluentActions.Invoking(() => ModelToolProtocol.CreateToolResult(call, reply))
            .Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaintenanceAndProviderSettingsCannotBeMixedIntoSettingsDiscovery(bool provider)
    {
        var reply = SettingsDiscoveryTools.Read([], SettingsDiscoveryTools.List, "{}");
        reply = provider ? reply with { ProviderSettings = new() } : reply with { Maintenance = Success().Maintenance };
        var input = CapabilityInputPolicy.Read(SettingsDiscoveryTools.FindExact(SettingsDiscoveryTools.List)!, "{}");

        SettingsDiscoveryTools.Matches(reply, SettingsDiscoveryTools.List, input).Should().BeFalse();
    }

    [Fact]
    public void ResultsCannotConfuseInstallerStartupWithInstallationOrAnotherVersion()
    {
        var result = new MaintenanceToolObservation("1.0.0", ReleaseChannel.Production, ReleaseAvailability.Available,
            "The verified installer was started; setup owns installation.", "1.2.3", InstallerStartOutcome.Started);
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, MaintenanceTools.ResultReason, Maintenance: result);
        ModelToolProtocol.CreateToolResult(new("upgrade_1", MaintenanceTools.Upgrade, "{}"), reply)
            .Result.Should().Equal(CapabilityReplyWire.Serialize(reply));
        MaintenanceTools.Matches(reply, MaintenanceTools.Upgrade, ReleaseVersion.Parse("1.2.3")).Should().BeTrue();
        MaintenanceTools.Matches(reply, MaintenanceTools.Upgrade, ReleaseVersion.Parse("1.2.4")).Should().BeFalse();
        MaintenanceTools.Matches(reply, MaintenanceTools.Check).Should().BeFalse();
        MaintenanceTools.Matches(reply with { Maintenance = result with { Installer = InstallerStartOutcome.Denied } },
            MaintenanceTools.Upgrade).Should().BeFalse();
        MaintenanceTools.Matches(reply with { Version = new("1.0.0", "observed") }, MaintenanceTools.Upgrade).Should().BeFalse();
        MaintenanceTools.IsExplicitRequest("maintenance upgrade latest").Should().BeTrue();
        MaintenanceTools.IsExplicitRequest("maintenance check 1.2.3").Should().BeTrue();
        MaintenanceTools.IsExplicitRequest("unrelated conversation").Should().BeFalse();
    }
}
