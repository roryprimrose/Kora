using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Mcp;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Tools;

public sealed partial class KoraMcpServerTests
{
    [Theory]
    [InlineData(ModelToolMode.HostedClarification)]
    [InlineData(ModelToolMode.LocalWebPage)]
    public void Required_disabled_tool_blocks_context_before_disclosure(ModelToolMode mode)
    {
        using var fixture = new Fixture();
        using var root = Root();
        var tool = mode == ModelToolMode.HostedClarification
            ? ModelToolProtocol.AskUserToolName : Kora.Core.Network.WebPageCapability.Id;
        var context = new ModelContextEnvelope(root.Request, "policy", "input", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15), mode);
        fixture.Preferences.RestrictContext(context).Should().BeSameAs(context);
        fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, tool, false);
        var prepare = () => fixture.Preferences.RestrictContext(context);
        prepare.Should().Throw<InvalidOperationException>();
        fixture.Info.Reads.Should().Be(0);
    }

    [Fact]
    public void Filtering_keeps_prior_transport_limits_and_does_not_change_foreign_or_tool_free_context()
    {
        using var fixture = new Fixture();
        using var root = Root();
        var constraints = new ModelTurnConstraints(1,
            [ReadOnlyCapabilityCatalog.Version, ReadOnlyCapabilityCatalog.List], 2, 3, 16000, 2000, 1000);
        var context = new ModelContextEnvelope(root.Request, "policy", "input", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.ReadOnly, constraints);
        fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, ReadOnlyCapabilityCatalog.Version, false);
        var filtered = fixture.Preferences.RestrictContext(context);
        filtered.Tools.Select(item => item.Id).Should().Equal(ReadOnlyCapabilityCatalog.List);
        filtered.Constraints!.MaximumCalls.Should().Be(2);
        filtered.Constraints.MaximumRounds.Should().Be(3);
        filtered.Constraints.InputUtf8Bytes.Should().Be(16000);
        filtered.Constraints.OutputUtf8Bytes.Should().Be(2000);
        filtered.Constraints.DeadlineMilliseconds.Should().Be(1000);
        foreach (var mode in new[] { ModelToolMode.None, ModelToolMode.McpReadOnly, ModelToolMode.LocalWebPage })
        {
            var other = new ModelContextEnvelope(root.Request, "policy", "input", [],
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15), mode,
                mcpTool: mode == ModelToolMode.McpReadOnly
                    ? new("mcp.search", """{"type":"object","additionalProperties":false,"properties":{},"required":[]}""") : null,
                webPages: mode == ModelToolMode.LocalWebPage
                    ? new([new Uri("https://example.com/")]) : null);
            fixture.Preferences.RestrictContext(other).Should().BeSameAs(other);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Failed_parent_or_category_write_closes_availability_and_requires_recovery(bool parent)
    {
        using var fixture = new Fixture();
        using var root = Root();
        var changes = 0;
        void Changed(object? sender, EventArgs args) => changes++;
        fixture.Preferences.Changed += Changed;
        fixture.Preferences.SetCategoryEnabled(KoraMcpCatalog.ServerId, McpCapabilityCategory.Tools, true);
        changes.Should().Be(1);
        fixture.Preferences.Changed -= Changed;
        fixture.Store.FailWrite = true;
        Action change = parent
            ? () => fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, false)
            : () => fixture.Preferences.SetCategoryEnabled(KoraMcpCatalog.ServerId, McpCapabilityCategory.Tools, false);
        change.Should().Throw<IOException>();
        fixture.Preferences.IsToolSelected(KoraMcpCatalog.ServerId, "settings.get").Should().BeFalse();
        fixture.Store.FailWrite = false;
        var recover = () => fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, "settings.get", true);
        recover.Should().Throw<InvalidOperationException>();
        changes.Should().Be(1);
    }

    [Fact]
    public void Composed_inventory_and_controls_use_one_policy_and_fail_for_unregistered_targets()
    {
        using var fixture = new Fixture();
        using var root = Root();
        var directory = Path.GetFullPath(Path.Combine("artifacts", "builtin-inventory-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "mcp.json");
            File.WriteAllText(file, """{"servers":{"remote":{"url":"https://example.com/mcp"}}}""");
            fixture.Configuration.Add(ArtifactSourceKind.McpServerConfiguration, file);
            var service = new McpServerConfigurationService(new InventoryPaths(directory), fixture.Directories,
                Path.Combine(directory, "profile"), capabilities: fixture.Preferences);
            var legacy = new McpServerConfigurationService(new InventoryPaths(directory), fixture.Directories,
                Path.Combine(directory, "profile"));
            legacy.LoadCapabilities().Single(item => item.IsBuiltIn).IsIncluded.Should().BeFalse();
            var missingControls = () => legacy.SetCapabilityEnabled(KoraMcpCatalog.ServerId, null, false);
            missingControls.Should().Throw<InvalidOperationException>();
            var inventory = service.LoadCapabilities();
            inventory.Should().HaveCount(2);
            inventory.Single(item => item.IsBuiltIn).IsIncluded.Should().BeTrue();
            var external = inventory.Single(item => !item.IsBuiltIn);
            service.SetCapabilityEnabled(external.Id, null, true);
            service.SetCapabilityEnabled(external.Id, "search", false);
            service.IsToolEnabled(external.Id, "search").Should().BeFalse();
            service.SetCapabilityEnabled(KoraMcpCatalog.ServerId, "settings.get", false);
            service.ReadCapabilitySelection().IsEnabled(KoraMcpCatalog.ServerId, McpCapabilityKind.Tool, "settings.get")
                .Should().BeFalse();
            service.SetCapabilityEnabled(KoraMcpCatalog.ServerId, null, false);
            service.LoadCapabilities().Single(item => item.IsBuiltIn).IsIncluded.Should().BeFalse();
            var unknownServer = () => service.SetCapabilityEnabled(new string('f', 64), null, false);
            unknownServer.Should().Throw<InvalidOperationException>();
            var unknownTool = () => service.SetCapabilityEnabled(KoraMcpCatalog.ServerId, "unknown", false);
            unknownTool.Should().Throw<InvalidOperationException>();
            fixture.Info.Reads.Should().Be(0);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"jsonrpc":2,"method":"notifications/initialized"}""")]
    [InlineData("""{"jsonrpc":"1.0","method":"notifications/initialized"}""")]
    [InlineData("""{"jsonrpc":"2.0","other":"notifications/initialized"}""")]
    [InlineData("""{"jsonrpc":"2.0","method":1}""")]
    [InlineData("""{"jsonrpc":"2.0","method":"other"}""")]
    public void Invalid_initialization_notifications_never_dispatch(string notification)
    {
        using var fixture = new Fixture();
        using var root = Root();
        var caller = fixture.Registry.Admit(CapabilityLane.Execution);
        var notify = () => fixture.Server.HandleInitialized(caller, Encoding.UTF8.GetBytes(notification), Token);
        notify.Should().Throw<InvalidDataException>();
        fixture.Info.Reads.Should().Be(0);
    }

    [Fact]
    public async Task Private_notifications_require_current_caller_and_late_async_results_require_unchanged_policy()
    {
        using var fixture = new Fixture();
        using var other = new Fixture();
        using var root = Root();
        var foreign = other.Registry.Admit(CapabilityLane.Execution);
        var notify = () => fixture.Server.HandleInitialized(foreign, McpProtocol.InitializedNotification(), Token);
        notify.Should().Throw<InvalidOperationException>();
        var caller = fixture.Registry.Admit(CapabilityLane.Execution);
        fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, false);
        var disabled = () => fixture.Server.HandleInitialized(caller, McpProtocol.InitializedNotification(), Token);
        disabled.Should().Throw<InvalidOperationException>();
        fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, true);
        var result = await fixture.Server.InvokeAsync(ModelToolMode.SettingsDiscovery, "settings.get", "{}",
            _ =>
            {
                fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, "settings.get", false);
                return ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Succeeded, "not-current"));
            }, Token);
        result.Reason.Should().Be("mcp-capability-admission-changed");
        result.Settings.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1}}""")]
    public void Initialization_requires_a_typed_protocol_version(string request)
    {
        using var fixture = new Fixture();
        using var root = Root();
        var caller = fixture.Registry.Admit(CapabilityLane.Execution);
        var invoke = () => fixture.Server.HandleRequest(caller, Encoding.UTF8.GetBytes(request), Token);
        invoke.Should().Throw<InvalidDataException>();
    }

    private sealed record InventoryPaths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }
}
