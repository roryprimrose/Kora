using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Application.Mcp;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Tools;

public sealed partial class KoraMcpServerTests
{
    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.HostSystem, true)]
    public async Task NativeMaintenanceRequiresOriginalUserOriginEvenForAValidNativeCaller(RequestOrigin origin, bool denied)
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var caller = f.Registry.Admit(CapabilityLane.Native);
        var maintenance = new MaintenanceRunner();
        var server = new KoraMcpServer(f.Registry, f.Preferences, maintenance);

        using var response = JsonDocument.Parse(await server.HandleRequestAsync(caller,
            MaintenanceRequest(MaintenanceTools.Check, "{}"), Token));

        response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean().Should().Be(denied);
        maintenance.Calls.Should().Be(denied ? 0 : 1);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("version-missing")]
    [InlineData("version-kind")]
    [InlineData("version-value")]
    [InlineData("id-missing")]
    [InlineData("id-kind")]
    [InlineData("id-large")]
    [InlineData("parameters-count")]
    [InlineData("arguments-missing")]
    [InlineData("method-missing")]
    [InlineData("method-kind")]
    [InlineData("parameters-missing")]
    [InlineData("parameters-kind")]
    [InlineData("name-missing")]
    [InlineData("name-kind")]
    public async Task MalformedAsyncMaintenanceEnvelopesCannotInvokeAnything(string field)
    {
        using var f = new Fixture();
        using var root = Root();
        var caller = f.Registry.Admit(CapabilityLane.Native);
        var maintenance = new MaintenanceRunner();
        var server = new KoraMcpServer(f.Registry, f.Preferences, maintenance);
        var json = JsonNode.Parse(MaintenanceRequest(MaintenanceTools.Upgrade, "{}"))!.AsObject();
        var parameters = json["params"]!.AsObject();
        switch (field)
        {
            case "count": json["extra"] = true; break;
            case "version-missing": json.Remove("jsonrpc"); json["other"] = true; break;
            case "version-kind": json["jsonrpc"] = 2; break;
            case "version-value": json["jsonrpc"] = "1.0"; break;
            case "id-missing": json.Remove("id"); json["other"] = true; break;
            case "id-kind": json["id"] = "10"; break;
            case "id-large": json["id"] = long.MaxValue; break;
            case "parameters-count": parameters["extra"] = true; break;
            case "arguments-missing": parameters.Remove("arguments"); parameters["other"] = true; break;
            case "method-missing": json.Remove("method"); break;
            case "method-kind": json["method"] = 10; break;
            case "parameters-missing": json.Remove("params"); break;
            case "parameters-kind": json["params"] = 10; break;
            case "name-missing": parameters.Remove("name"); break;
            case "name-kind": parameters["name"] = 10; break;
        }
        var invoke = () => server.HandleRequestAsync(caller, System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), Token);
        await invoke.Should().ThrowAsync<InvalidDataException>();
        maintenance.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(10000000)]
    public async Task AsyncMaintenanceRejectsInvalidResponseBounds(int bound)
    {
        using var f = new Fixture();
        using var root = Root();
        var caller = f.Registry.Admit(CapabilityLane.Native);
        var invoke = () => f.Server.HandleRequestAsync(caller, MaintenanceRequest(MaintenanceTools.Check, "{}"), Token, bound);
        await invoke.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AsyncMaintenanceHonorsCancellationServerDisableAndWholeResponseBounds()
    {
        using var f = new Fixture();
        using var root = Root();
        var caller = f.Registry.Admit(CapabilityLane.Native);
        var server = new KoraMcpServer(f.Registry, f.Preferences, new MaintenanceRunner());
        var small = () => server.HandleRequestAsync(caller, MaintenanceRequest(MaintenanceTools.Check, "{}"), Token, 1);
        await small.Should().ThrowAsync<InvalidDataException>();
        f.Preferences.SetServerEnabled(Kora.Core.Mcp.KoraMcpCatalog.ServerId, false);
        var disabled = () => server.HandleRequestAsync(caller, MaintenanceRequest(MaintenanceTools.Check, "{}"), Token);
        await disabled.Should().ThrowAsync<InvalidOperationException>();
        var invalidRoot = () => server.HandleRequestAsync(caller, "[]"u8.ToArray(), Token);
        f.Preferences.SetServerEnabled(Kora.Core.Mcp.KoraMcpCatalog.ServerId, true);
        await invalidRoot.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task AsyncMaintenanceCannotPublishAfterCapabilityPolicyChanges()
    {
        using var f = new Fixture();
        using var root = Root();
        var caller = f.Registry.Admit(CapabilityLane.Native);
        var maintenance = new MaintenanceRunner { DuringCall = () => f.Preferences.SetServerEnabled(Kora.Core.Mcp.KoraMcpCatalog.ServerId, false) };
        var server = new KoraMcpServer(f.Registry, f.Preferences, maintenance);
        var invoke = () => server.HandleRequestAsync(caller, MaintenanceRequest(MaintenanceTools.Check, "{}"), Token);
        await invoke.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeMcpMaintenanceInvokesTheExactVersionAndNeverClaimsInstalled(bool upgrade)
    {
        using var f = new Fixture();
        using var root = Root();
        var caller = f.Registry.Admit(CapabilityLane.Native);
        var maintenance = new MaintenanceRunner();
        var server = new KoraMcpServer(f.Registry, f.Preferences, maintenance);
        var id = upgrade ? MaintenanceTools.Upgrade : MaintenanceTools.Check;

        using var response = JsonDocument.Parse(await server.HandleRequestAsync(caller,
            MaintenanceRequest(id, """{"version":"1.2.3"}"""), Token));

        response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeFalse();
        maintenance.Calls.Should().Be(1);
        maintenance.Id.Should().Be(id);
        maintenance.Input.Should().Be("""{"version":"1.2.3"}""");
        maintenance.Eligible.Should().BeTrue();
        response.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()
            .Should().Contain(upgrade ? "Started" : "Available").And.NotContain("\"Installed\"");
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("unbound")]
    [InlineData("lane")]
    [InlineData("path")]
    [InlineData("approval")]
    [InlineData("null-version")]
    public async Task UnadmittedMaintenanceCallsNeverReachTheInstallerRunner(string boundary)
    {
        using var f = new Fixture();
        using var root = Root();
        var caller = f.Registry.Admit(boundary is "lane" ? CapabilityLane.Execution : CapabilityLane.Native);
        var maintenance = new MaintenanceRunner();
        var server = new KoraMcpServer(f.Registry, f.Preferences, boundary is "unbound" ? null : maintenance);
        if (boundary is "disabled") { f.Preferences.SetToolEnabled(Kora.Core.Mcp.KoraMcpCatalog.ServerId, MaintenanceTools.Upgrade, false); }
        var input = boundary switch
        {
            "path" => """{"path":"Q:\\installer.exe"}""",
            "approval" => """{"approved":true}""",
            "null-version" => """{"version":null}""",
            _ => "{}",
        };

        using var response = JsonDocument.Parse(await server.HandleRequestAsync(caller,
            MaintenanceRequest(MaintenanceTools.Upgrade, input), Token));

        response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeTrue();
        maintenance.Calls.Should().Be(0);
    }

    [Fact]
    public async Task AsyncMcpPreservesExistingReadOnlyCallsAndRejectsForgedMaintenanceResults()
    {
        using var f = new Fixture();
        using var root = Root();
        var caller = f.Registry.Admit(CapabilityLane.Native);
        var maintenance = new MaintenanceRunner { WrongVersion = true };
        var server = new KoraMcpServer(f.Registry, f.Preferences, maintenance);
        using var version = JsonDocument.Parse(await server.HandleRequestAsync(caller,
            MaintenanceRequest(ReadOnlyCapabilityCatalog.Version, "{}"), Token));
        version.RootElement.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeFalse();
        using var rejected = JsonDocument.Parse(await server.HandleRequestAsync(caller,
            MaintenanceRequest(MaintenanceTools.Check, """{"version":"1.2.3"}"""), Token));
        rejected.RootElement.GetProperty("result").GetProperty("isError").GetBoolean().Should().BeTrue();
    }

    private static byte[] MaintenanceRequest(string name, string input) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        jsonrpc = "2.0", id = 10, method = "tools/call",
        @params = new { name, arguments = JsonSerializer.Deserialize<JsonElement>(input) },
    });

    private sealed class MaintenanceRunner : IApplicationMaintenanceMcp
    {
        public int Calls { get; private set; }
        public string? Id { get; private set; }
        public string? Input { get; private set; }
        public bool Eligible { get; private set; }
        public bool WrongVersion { get; init; }
        public Action? DuringCall { get; init; }
        public ValueTask<CapabilityReply> InvokeAsync(string id, string input, Func<bool> admitted, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            Id = id;
            Input = input;
            Eligible = admitted();
            DuringCall?.Invoke();
            return ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Succeeded, MaintenanceTools.ResultReason,
                Maintenance: new("1.0.0", ReleaseChannel.Production, ReleaseAvailability.Available,
                    "Verified canonical release; installer startup is not completed installation.",
                    WrongVersion ? "1.2.4" : "1.2.3", string.Equals(id, MaintenanceTools.Upgrade, StringComparison.Ordinal)
                        ? InstallerStartOutcome.Started : null)));
        }
    }
}
