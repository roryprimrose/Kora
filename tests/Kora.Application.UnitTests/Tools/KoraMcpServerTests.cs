using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Mcp;
using Kora.Application.Tools;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Tools;
using Kora.Core.Configuration;
using Kora.Core.Artifacts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Tools;

[Collection("Host tracing")]
public sealed partial class KoraMcpServerTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public KoraMcpServerTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public void Private_server_negotiates_discovers_and_invokes_the_actual_host_action()
    {
        using var fixture = new Fixture();
        using var root = Root();
        var caller = fixture.Registry.Admit(CapabilityLane.Execution);
        McpProtocol.ValidateInitialization(fixture.Server.HandleRequest(caller, McpProtocol.InitializeRequest(), Token));
        fixture.Server.HandleInitialized(caller, McpProtocol.InitializedNotification(), Token);
        using var discovery = JsonDocument.Parse(fixture.Server.HandleRequest(caller, McpProtocol.DiscoveryRequest(), Token));
        discovery.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()).Should()
            .Equal(ReadOnlyCapabilityCatalog.Descriptors.Select(tool => tool.Id));
        using var result = Call(fixture, caller, ReadOnlyCapabilityCatalog.Version, "{}");
        result.RootElement.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeFalse();
        var text = result.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        text.Should().Contain("observed-version");
        fixture.Info.Reads.Should().Be(1);
        HostActivity.Current.Should().BeSameAs(root);
    }

    [Fact]
    public void Disabled_tool_is_absent_and_cached_calls_cannot_execute_it()
    {
        using var fixture = new Fixture();
        using var root = Root();
        var caller = fixture.Registry.Admit(CapabilityLane.Execution);
        fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, ReadOnlyCapabilityCatalog.Version, false);
        using var discovery = JsonDocument.Parse(fixture.Server.HandleRequest(caller, McpProtocol.DiscoveryRequest(), Token));
        discovery.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength().Should().Be(5);
        using var reply = Call(fixture, caller, ReadOnlyCapabilityCatalog.Version, "{}");
        reply.RootElement.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeTrue();
        fixture.Info.Reads.Should().Be(0);
        fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.Version, "{}", Token)
            .Reason.Should().Be("mcp-capability-disabled");
        fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, ReadOnlyCapabilityCatalog.Version, true);
        fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.Version, "{}", Token)
            .Outcome.Should().Be(CapabilityOutcome.Succeeded);
    }

    [Fact]
    public void Server_disable_does_not_erase_per_tool_choices_and_survives_reload()
    {
        using var fixture = new Fixture();
        using var root = Root();
        fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, "settings.get", false);
        fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, false);
        fixture.Preferences.IsToolEnabled(KoraMcpCatalog.ServerId, ReadOnlyCapabilityCatalog.Version).Should().BeFalse();
        var reloaded = new McpCapabilityPreferences(new ArtifactUsePreferences(fixture.Store),
            fixture.Directories, fixture.Configuration);
        reloaded.IsServerEnabled(KoraMcpCatalog.ServerId).Should().BeFalse();
        reloaded.IsToolSelected(KoraMcpCatalog.ServerId, "settings.get").Should().BeFalse();
        reloaded.SetServerEnabled(KoraMcpCatalog.ServerId, true);
        reloaded.IsToolEnabled(KoraMcpCatalog.ServerId, ReadOnlyCapabilityCatalog.Version).Should().BeTrue();
        reloaded.IsToolEnabled(KoraMcpCatalog.ServerId, "settings.get").Should().BeFalse();
        reloaded.IsToolSelected(new string('a', 64), "settings.get").Should().BeTrue();
        fixture.Audit.Events.Count(entry => entry.Outcome == SecurityAuditOutcome.Requested).Should().Be(3);
        fixture.Audit.Events.Count(entry => entry.Outcome == SecurityAuditOutcome.Succeeded).Should().Be(3);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Saved_builtin_disable_survives_legacy_directory_migration_and_defaults_reload(int version)
    {
        using var root = Root();
        var store = new MemoryStore();
        var legacy = JsonNode.Parse(
            """{"Version":2,"Skills":[],"Instructions":[],"Prompts":[],"Agents":[],"McpConfigurationFiles":[],"IncludedMcpServerIds":[]}""")!;
        legacy["Version"] = version;
        if (version == 3) { legacy["UseDefaultDirectories"] = true; }
        store.WriteText("artifact-directories.json", legacy.ToJsonString());
        var paths = new Paths();
        var defaults = ArtifactDirectorySettings.DefaultDirectories(paths.RoamingRoot,
            Path.Combine(paths.LocalRoot, "editor"), Path.Combine(paths.LocalRoot, "user"));
        var directories = new LocalArtifactDirectoryPreferences(store, defaults);
        var audit = new AuditLog();
        var configuration = new ArtifactDirectoryConfigurationService(directories, audit);
        var preferences = new McpCapabilityPreferences(new ArtifactUsePreferences(store), directories, configuration);
        preferences.IsServerEnabled(KoraMcpCatalog.ServerId).Should().BeTrue();
        preferences.SetToolEnabled(KoraMcpCatalog.ServerId, "settings.get", false);
        preferences.SetServerEnabled(KoraMcpCatalog.ServerId, false);
        configuration.Add(ArtifactSourceKind.Skill, Path.Combine(paths.LocalRoot, "additional"));
        var restartedDirectories = new LocalArtifactDirectoryPreferences(store, defaults);
        var restarted = new McpCapabilityPreferences(new ArtifactUsePreferences(store), restartedDirectories,
            new ArtifactDirectoryConfigurationService(restartedDirectories, audit));
        restarted.IsServerEnabled(KoraMcpCatalog.ServerId).Should().BeFalse();
        restarted.IsToolSelected(KoraMcpCatalog.ServerId, "settings.get").Should().BeFalse();
        store.ReadText("artifact-directories.json").Should().Contain("\"Version\":4");
    }

    [Fact]
    public void Historical_source_use_veto_remains_effective_and_is_not_rewritten_by_consideration_changes()
    {
        using var fixture = new Fixture();
        using var root = Root();
        fixture.LegacyUse.SetDisabled(ArtifactUseIdentity.Mcp(KoraMcpCatalog.ServerId), true);
        var savedVeto = fixture.Store.ReadText("artifact-use.json");
        fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, true);
        fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, "settings.get", true);
        fixture.Directories.Load().IncludedMcpServerIds.Should().Contain(KoraMcpCatalog.ServerId);
        fixture.Preferences.IsToolSelected(KoraMcpCatalog.ServerId, "settings.get").Should().BeTrue();
        fixture.Preferences.IsServerEnabled(KoraMcpCatalog.ServerId).Should().BeFalse();
        fixture.Preferences.IsToolEnabled(KoraMcpCatalog.ServerId, "settings.get").Should().BeFalse();
        fixture.LegacyUse.IsDisabled(ArtifactUseIdentity.Mcp(KoraMcpCatalog.ServerId)).Should().BeTrue();
        fixture.Store.ReadText("artifact-use.json").Should().Be(savedVeto);
    }

    [Fact]
    public void Shared_category_and_item_policy_preserves_parent_state_and_atomic_saved_choices()
    {
        using var fixture = new Fixture();
        using var root = Root();
        fixture.Configuration.SetMcpItemEnabled(KoraMcpCatalog.ServerId, McpCapabilityKind.Tool, "settings.get", false);
        fixture.Preferences.IsServerEnabled(KoraMcpCatalog.ServerId).Should().BeTrue();
        fixture.Preferences.IsToolEnabled(KoraMcpCatalog.ServerId, "settings.get").Should().BeFalse();
        fixture.Preferences.SetCategoryEnabled(KoraMcpCatalog.ServerId, McpCapabilityCategory.Tools, false);
        fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, "settings.get", true);
        fixture.Preferences.IsToolEnabled(KoraMcpCatalog.ServerId, "settings.get").Should().BeTrue();
        fixture.Preferences.IsToolEnabled(KoraMcpCatalog.ServerId, "settings.list").Should().BeFalse();
        var before = fixture.Preferences.Revision;
        fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, false);
        var restartedDirectories = new LocalArtifactDirectoryPreferences(fixture.Store);
        var restarted = new McpCapabilityPreferences(new ArtifactUsePreferences(fixture.Store),
            restartedDirectories, new ArtifactDirectoryConfigurationService(restartedDirectories, fixture.Audit));
        restarted.IsServerEnabled(KoraMcpCatalog.ServerId).Should().BeFalse();
        restarted.IsToolSelected(KoraMcpCatalog.ServerId, "settings.get").Should().BeTrue();
        restarted.SetServerEnabled(KoraMcpCatalog.ServerId, true);
        restarted.IsToolEnabled(KoraMcpCatalog.ServerId, "settings.get").Should().BeTrue();
        restarted.IsToolEnabled(KoraMcpCatalog.ServerId, "settings.list").Should().BeFalse();
        restarted.Revision.Should().NotBe(before);
        restarted.SetCategoryEnabled(KoraMcpCatalog.ServerId, McpCapabilityCategory.Tools, true);
        restartedDirectories.Load().McpCapabilities.Categories.Single(category => category.Category == McpCapabilityCategory.Tools)
            .Items.Should().BeEmpty();
        fixture.Store.Names.Should().Equal("artifact-directories.json");
    }

    [Fact]
    public void Reserved_server_identity_is_not_an_item_digest_or_an_arbitrary_symbolic_server()
    {
        McpCapabilitySelection.Default.SetItem(KoraMcpCatalog.ServerId, McpCapabilityKind.Tool, "settings.get", false)
            .IsEnabled(KoraMcpCatalog.ServerId, McpCapabilityKind.Tool, "settings.get").Should().BeFalse();
        var invalidKey = () => new McpItemSelection(KoraMcpCatalog.ServerId, true);
        invalidKey.Should().Throw<ArgumentException>();
        var invalidServer = () => McpCapabilitySelection.Default.SetCategory("builtin.foreign", McpCapabilityCategory.Tools, true);
        invalidServer.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Built_in_display_projection_is_pure_metadata_without_preference_or_transport_reads()
    {
        using var fixture = new Fixture();
        var paths = new Paths();
        var service = new McpServerConfigurationService(paths, fixture.Directories,
            Path.Combine(paths.LocalRoot, "profile"), capabilities: fixture.Preferences);
        fixture.Store.FailRead = true;
        var tools = service.ReadBuiltInTools();
        tools.Select(tool => tool.Id).Should().Equal(KoraMcpCatalog.ToolIds);
        tools.Should().OnlyContain(tool => tool.Kind == McpCapabilityKind.Tool);
        foreach (var tool in tools)
        {
            using var definition = JsonDocument.Parse(tool.DefinitionJson);
            definition.RootElement.GetProperty("name").GetString().Should().Be(tool.Id);
            definition.RootElement.GetProperty("inputSchema").ValueKind.Should().Be(JsonValueKind.Object);
            (tool.UnavailableReason is null).Should().Be(ReadOnlyCapabilityCatalog.FindExact(tool.Id) is not null);
            tool.Category.Should().Be(McpCapabilityCategory.Tools);
            tool.ToString().Should().Be(tool.Name
                + (tool.UnavailableReason is null ? string.Empty : " (unavailable: " + tool.UnavailableReason + ")"));
        }
        fixture.Info.Reads.Should().Be(0);
        fixture.Store.Names.Should().BeEmpty();
    }

    [Fact]
    public void Malformed_saved_item_identity_cannot_be_ignored_as_an_enabled_default()
    {
        using var fixture = new Fixture();
        using var root = Root();
        fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, "settings.get", false);
        var saved = JsonNode.Parse(fixture.Store.ReadText("artifact-directories.json")!)!;
        saved["McpCapabilities"]![0]!["Items"]![0]!["Key"] = KoraMcpCatalog.ServerId;
        fixture.Store.WriteText("artifact-directories.json", saved.ToJsonString());
        var read = () => fixture.Directories.Load();
        read.Should().Throw<InvalidDataException>();
        var caller = fixture.Registry.Admit(CapabilityLane.Execution);
        var invoke = () => fixture.Registry.Invoke(caller, ReadOnlyCapabilityCatalog.Version, "{}", Token);
        invoke.Should().Throw<InvalidDataException>();
        fixture.Info.Reads.Should().Be(0);
    }

    [Fact]
    public void Complete_private_mcp_response_obeys_the_exact_host_byte_budget()
    {
        using var fixture = new Fixture();
        using var root = Root();
        var caller = fixture.Registry.Admit(CapabilityLane.Execution);
        var request = McpProtocol.DiscoveryRequest();
        var expected = fixture.Server.HandleRequest(caller, request, Token);
        fixture.Server.HandleRequest(caller, request, Token, expected.Length).Should().Equal(expected);
        var tooSmall = () => fixture.Server.HandleRequest(caller, request, Token, expected.Length - 1);
        tooSmall.Should().Throw<InvalidDataException>();
        var zero = () => fixture.Server.HandleRequest(caller, request, Token, 0);
        zero.Should().Throw<ArgumentOutOfRangeException>();
        var enlarged = () => fixture.Server.HandleRequest(caller, request, Token, McpProtocol.MaximumDiscoveryUtf8Bytes + 1);
        enlarged.Should().Throw<ArgumentOutOfRangeException>();
        fixture.Info.Reads.Should().Be(0);
    }

    [Fact]
    public void Disabled_server_foreign_caller_and_cancellation_have_no_tool_effect()
    {
        using var fixture = new Fixture();
        using var other = new Fixture();
        using var root = Root();
        var foreign = other.Registry.Admit(CapabilityLane.Execution);
        var call = () => fixture.Server.HandleRequest(foreign, McpProtocol.DiscoveryRequest(), Token);
        call.Should().Throw<InvalidOperationException>();
        var caller = fixture.Registry.Admit(CapabilityLane.Execution);
        fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, false);
        var disabled = () => fixture.Server.HandleRequest(caller, McpProtocol.DiscoveryRequest(), Token);
        disabled.Should().Throw<InvalidOperationException>();
        fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => fixture.Server.HandleRequest(caller, McpProtocol.DiscoveryRequest(), cancellation.Token);
        cancelled.Should().Throw<OperationCanceledException>();
        fixture.Info.Reads.Should().Be(0);
    }

    [Fact]
    public void Disable_and_reenable_during_action_prevents_late_result_publication()
    {
        using var fixture = new Fixture();
        using var root = Root();
        var caller = fixture.Registry.Admit(CapabilityLane.Execution);
        fixture.Info.OnRead = () =>
        {
            fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, false);
            fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, true);
        };
        var call = () => Call(fixture, caller, ReadOnlyCapabilityCatalog.Version, "{}");
        call.Should().Throw<InvalidOperationException>();
        fixture.Info.Reads.Should().Be(1);
    }

    [Theory]
    [InlineData(ModelToolMode.ReadOnly)]
    [InlineData(ModelToolMode.SettingsDiscovery)]
    [InlineData(ModelToolMode.CustomizationDiscovery)]
    [InlineData(ModelToolMode.SessionDiscovery)]
    public void Disabled_tools_are_removed_before_context_serialization_without_expanding_the_mode(ModelToolMode mode)
    {
        using var fixture = new Fixture();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var envelope = new ModelContextEnvelope(request, "policy", "input", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15), mode);
        fixture.Preferences.RestrictContext(envelope).Should().BeSameAs(envelope);
        var tool = envelope.Tools[0].Id;
        fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, tool, false);
        var restricted = fixture.Preferences.RestrictContext(envelope);
        restricted.ToolMode.Should().Be(mode);
        restricted.SystemPolicy.Should().Be(envelope.SystemPolicy);
        restricted.UserRequest.Should().Be(envelope.UserRequest);
        restricted.Tools.Should().NotContain(item => string.Equals(item.Id, tool, StringComparison.Ordinal));
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var roundTrip = ModelToolProtocol.ReadContext(restricted.Serialize(), provenance);
        roundTrip.Tools.Select(item => item.Id).Should().Equal(restricted.Tools.Select(item => item.Id));
        roundTrip.Constraints!.Version.Should().Be(2);
        fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, false);
        fixture.Preferences.RestrictContext(envelope).Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task Source_bound_dispatch_is_the_same_server_but_cannot_acquire_another_modes_tools()
    {
        using var fixture = new Fixture();
        using var root = Root();
        var calls = 0;
        ValueTask<CapabilityReply> Invoke(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            calls++;
            return ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Succeeded, "reviewed"));
        }
        (await fixture.Server.InvokeAsync(ModelToolMode.ReadOnly, "settings.get", "{}", Invoke, Token))
            .Reason.Should().Be("model-tools-not-permitted");
        calls.Should().Be(0);
        (await fixture.Server.InvokeAsync(ModelToolMode.SettingsDiscovery, "settings.get", "{}", Invoke, Token))
            .Reason.Should().Be("reviewed");
        calls.Should().Be(1);
        fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, "settings.get", false);
        (await fixture.Server.InvokeAsync(ModelToolMode.SettingsDiscovery, "settings.get", "{}", Invoke, Token))
            .Reason.Should().Be("mcp-capability-disabled");
        calls.Should().Be(1);
    }

    [Fact]
    public void Failed_preference_write_disables_use_and_cannot_emit_a_success_audit()
    {
        using var fixture = new Fixture();
        using var root = Root();
        fixture.Store.FailWrite = true;
        var change = () => fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, "settings.get", false);
        change.Should().Throw<IOException>();
        fixture.Preferences.IsToolEnabled(KoraMcpCatalog.ServerId, ReadOnlyCapabilityCatalog.Version).Should().BeFalse();
        fixture.Audit.Events.Select(entry => entry.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"jsonrpc":2,"id":1,"method":"tools/list","params":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":"1","method":"tools/list","params":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{"extra":true}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":1,"arguments":{}}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"unknown"}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"unknown","params":{}}""")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"id":2,"method":"tools/list","params":{}}""")]
    public void Invalid_requests_are_rejected_without_dispatch(string request)
    {
        using var fixture = new Fixture();
        using var root = Root();
        var caller = fixture.Registry.Admit(CapabilityLane.Execution);
        var invoke = () => fixture.Server.HandleRequest(caller, Encoding.UTF8.GetBytes(request), Token);
        invoke.Should().Throw<InvalidDataException>();
        fixture.Info.Reads.Should().Be(0);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static HostActivity Root() =>
        HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);

    private static JsonDocument Call(Fixture fixture, CapabilityCaller caller, string name, string arguments) =>
        JsonDocument.Parse(fixture.Server.HandleRequest(caller, JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0", id = 3, method = "tools/call",
            @params = new { name, arguments = JsonSerializer.Deserialize<JsonElement>(arguments) },
        }), Token));

    private sealed class Fixture : IDisposable
    {
        public MemoryStore Store { get; } = new();
        public AuditLog Audit { get; } = new();
        public McpCapabilityPreferences Preferences { get; }
        public ArtifactUsePreferences LegacyUse { get; }
        public LocalArtifactDirectoryPreferences Directories { get; }
        public ArtifactDirectoryConfigurationService Configuration { get; }
        public AppInfo Info { get; } = new();
        public ReadOnlyCapabilityRegistry Registry { get; }
        public KoraMcpServer Server { get; }
        private readonly DependencyBootstrapper dependencies = new([], NullLogger<DependencyBootstrapper>.Instance);

        public Fixture()
        {
            Directories = new(Store);
            Configuration = new(Directories, Audit);
            LegacyUse = new(Store);
            Preferences = new(LegacyUse, Directories, Configuration);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            Registry = new(new HostAccess(), new Kora.Tools.Capabilities.CapabilitiesList(),
                new Kora.Tools.Capabilities.CapabilitiesGet(), new Kora.Tools.Application.ApplicationGetVersion(Info),
                new Kora.Tools.Readiness.ReadinessGet(dependencies), new Kora.Tools.Runtime.RuntimeList(runtime),
                new Kora.Tools.Runtime.RuntimeGetStatus(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance, Preferences);
            Server = new(Registry, Preferences);
        }

        public void Dispose() => dependencies.Dispose();
    }

    private sealed class HostAccess : ICapabilityHostAccess
    {
        public bool IsCurrentHost => true;
    }

    private sealed class AppInfo : IApplicationInfo
    {
        public int Reads { get; private set; }
        public Action? OnRead { get; set; }
        public string Version { get { Reads++; OnRead?.Invoke(); return "observed-version"; } }
    }

    internal sealed class MemoryStore : IPreferenceStore
    {
        private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
        public IReadOnlyCollection<string> Names => files.Keys;
        public bool FailWrite { get; set; }
        public bool FailRead { get; set; }
        public string? ReadText(string fileName)
        {
            if (FailRead) { throw new IOException("Synthetic read must not occur."); }
            return files.GetValueOrDefault(fileName);
        }
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteText(string fileName, string contents)
        {
            if (FailWrite) { throw new IOException("Synthetic preference write failure."); }
            files[fileName] = contents;
        }

        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
        public void Delete(string fileName) => files.Remove(fileName);
    }

    private sealed class Paths : IApplicationDataPaths
    {
        public string LocalRoot { get; } = Path.GetFullPath(Path.Combine("artifacts", "kora-mcp-metadata-not-created"));
        public string RoamingRoot => LocalRoot;
    }

    private sealed class AuditLog : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
