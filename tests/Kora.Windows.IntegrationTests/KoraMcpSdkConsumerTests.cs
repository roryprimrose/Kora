using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Mcp;
using Kora.Application.Tools;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class KoraMcpSdkConsumerTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public KoraMcpSdkConsumerTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OfficialConsumerNegotiatesDiscoversAndInvokesOnlyCurrentHostCapabilities()
    {
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        using var fixture = new Fixture();
        using var handler = fixture.Handler();
        using var http = new HttpClient(handler);
        await using var transport = Transport(http);
        await using var client = await McpClient.CreateAsync(transport, Options(), cancellationToken: Token);

        client.NegotiatedProtocolVersion.Should().Be(McpProtocol.Version);
        client.ServerCapabilities.Tools.Should().NotBeNull();
        var page = await client.ListToolsAsync(new ListToolsRequestParams(), Token);
        page.Tools.Select(tool => tool.Name).Should().Equal(
            ReadOnlyCapabilityCatalog.Descriptors.Concat(MaintenanceTools.Descriptors).Select(tool => tool.Id));
        var result = await client.CallToolAsync(new CallToolRequestParams
        {
            Name = ReadOnlyCapabilityCatalog.Version,
            Arguments = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal),
        }, Token);
        result.IsError.Should().BeFalse();
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Contain("observed-version");
        fixture.Info.Reads.Should().Be(1);
        HostActivity.Current.Should().BeSameAs(root);

        fixture.Preferences.SetToolEnabled(KoraMcpCatalog.ServerId, ReadOnlyCapabilityCatalog.Version, false);
        (await client.ListToolsAsync(new ListToolsRequestParams(), Token)).Tools.Should()
            .NotContain(tool => tool.Name == ReadOnlyCapabilityCatalog.Version);
        var denied = await client.CallToolAsync(new CallToolRequestParams
        {
            Name = ReadOnlyCapabilityCatalog.Version,
            Arguments = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal),
        }, Token);
        denied.IsError.Should().BeTrue();
        fixture.Info.Reads.Should().Be(1);
        handler.Methods.Should().Equal("initialize", "notifications/initialized", "tools/list", "tools/call", "tools/list", "tools/call");
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("disabled")]
    [InlineData("bytes")]
    public async Task OfficialConsumerCannotInitializeOutsideThePrivateHostBoundary(string fault)
    {
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        using var fixture = new Fixture();
        using var other = new Fixture();
        using var handler = fault is "foreign" ? new KoraMcpSdkClientTransport(fixture.Server,
            other.Registry.Admit(CapabilityLane.Execution)) : fixture.Handler();
        if (fault is "disabled") { fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, false); }
        if (fault is "bytes") { handler.MaximumResponseBytes = 1; }
        using var http = new HttpClient(handler);
        await using var transport = Transport(http);
        async Task Initialize()
        {
            await using var client = await McpClient.CreateAsync(transport, Options(), cancellationToken: Token);
        }
        if (fault is "bytes") { await FluentActions.Awaiting(Initialize).Should().ThrowAsync<InvalidDataException>(); }
        else { await FluentActions.Awaiting(Initialize).Should().ThrowAsync<InvalidOperationException>(); }
        handler.Methods.Should().Equal("initialize");
        fixture.Info.Reads.Should().Be(0);
    }

    [Fact]
    public async Task OfficialConsumerCannotPublishAResultAfterHostPolicyChangesDuringDispatch()
    {
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        using var fixture = new Fixture();
        using var handler = fixture.Handler();
        using var http = new HttpClient(handler);
        await using var transport = Transport(http);
        await using var client = await McpClient.CreateAsync(transport, Options(), cancellationToken: Token);
        fixture.Info.OnRead = () => fixture.Preferences.SetServerEnabled(KoraMcpCatalog.ServerId, false);
        await FluentActions.Awaiting(async () => await client.CallToolAsync(new CallToolRequestParams
        {
            Name = ReadOnlyCapabilityCatalog.Version,
            Arguments = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal),
        }, Token)).Should().ThrowAsync<InvalidOperationException>();
        fixture.Info.Reads.Should().Be(1);
        handler.Methods.Should().Equal("initialize", "notifications/initialized", "tools/call");
    }

    private static McpClientOptions Options() => new()
    {
        ProtocolVersion = McpProtocol.Version,
        ClientInfo = new() { Name = "Kora-test", Version = "1" },
        Capabilities = new(),
    };

    private static HttpClientTransport Transport(HttpClient http) => new(new()
    {
        Endpoint = new("https://private-dispatcher.invalid/mcp"),
        TransportMode = HttpTransportMode.StreamableHttp,
        EnableStandaloneGetStream = false,
        OwnsSession = false,
        MaxReconnectionAttempts = 0,
    }, http);

    private sealed class Fixture : IDisposable
    {
        private readonly DependencyBootstrapper dependencies = new([], NullLogger<DependencyBootstrapper>.Instance);
        internal Info Info { get; } = new();
        internal McpCapabilityPreferences Preferences { get; }
        internal ReadOnlyCapabilityRegistry Registry { get; }
        internal KoraMcpServer Server { get; }
        internal Fixture()
        {
            var store = new Store();
            var directories = new LocalArtifactDirectoryPreferences(store);
            var configuration = new ArtifactDirectoryConfigurationService(directories, new Audit());
            Preferences = new(new ArtifactUsePreferences(store), directories, configuration);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            Registry = new(new HostAccess(), new Kora.Tools.Capabilities.CapabilitiesList(),
                new Kora.Tools.Capabilities.CapabilitiesGet(), new Kora.Tools.Application.ApplicationGetVersion(Info),
                new Kora.Tools.Readiness.ReadinessGet(dependencies), new Kora.Tools.Runtime.RuntimeList(runtime),
                new Kora.Tools.Runtime.RuntimeGetStatus(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance, Preferences);
            Server = new(Registry, Preferences);
        }
        internal KoraMcpSdkClientTransport Handler() => new(Server, Registry.Admit(CapabilityLane.Execution));
        public void Dispose() => dependencies.Dispose();
    }

    private sealed class Info : IApplicationInfo
    {
        internal int Reads { get; private set; }
        internal Action? OnRead { get; set; }
        public string Version { get { Reads++; OnRead?.Invoke(); return "observed-version"; } }
    }
    private sealed class HostAccess : ICapabilityHostAccess { public bool IsCurrentHost => true; }
    private sealed class Audit : ISecurityAuditLog { public void Write(SecurityAuditEvent auditEvent) { } }
    private sealed class Store : IPreferenceStore
    {
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
        public string? ReadText(string fileName) => values.GetValueOrDefault(fileName);
        public void WriteText(string fileName, string contents) => values[fileName] = contents;
        public void Delete(string fileName) => values.Remove(fileName);
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
    }
}
