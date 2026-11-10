using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Core.Tools;
using Kora.Tools.Application;

namespace Kora.Tools.UnitTests.Application;

public sealed class ApplicationMaintenanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McpActionsUseTheSharedHostRunnerWithoutApprovalArguments(bool upgrade)
    {
        var host = new McpMaintenance();
        Func<bool> admitted = () => true;
        var token = TestContext.Current.CancellationToken;
        var result = upgrade
            ? await new ApplicationUpgrade(host).ExecuteMcpAsync("""{"version":"1.2.3"}""", admitted, token)
            : await new ApplicationCheckForUpdates(host).ExecuteMcpAsync("""{"version":"1.2.3"}""", admitted, token);
        host.Id.Should().Be(upgrade ? MaintenanceTools.Upgrade : MaintenanceTools.Check);
        host.Input.Should().Be("""{"version":"1.2.3"}""");
        host.Admitted.Should().BeSameAs(admitted);
        host.Token.Should().Be(token);
        result.Reason.Should().Be("recorded");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingMcpRunnerIsExplicitlyUnavailable(bool upgrade)
    {
        var host = new Maintenance();
        var result = upgrade
            ? await new ApplicationUpgrade(host).ExecuteMcpAsync("{}", () => true, TestContext.Current.CancellationToken)
            : await new ApplicationCheckForUpdates(host).ExecuteMcpAsync("{}", () => true, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(CapabilityOutcome.Denied);
        host.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Actions_share_the_host_contract_and_never_receive_an_executable_or_url(bool upgrade, bool exact)
    {
        var host = new Maintenance();
        var version = exact ? ReleaseVersion.Parse("1.2.3-beta7") : null;
        Func<bool> admitted = () => true;
        var token = TestContext.Current.CancellationToken;
        var response = upgrade
            ? await new ApplicationUpgrade(host).ExecuteAsync(version, RequestOrigin.ActivatedVoice, admitted, token)
            : await new ApplicationCheckForUpdates(host).ExecuteAsync(version, RequestOrigin.ActivatedVoice, admitted, token);
        response.Should().Be(upgrade ? "native review required" : "checked");
        host.Version.Should().BeSameAs(version);
        host.Admitted.Should().BeSameAs(admitted);
        host.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        host.Token.Should().Be(token);
        host.Calls.Should().Be(1);
        ApplicationCheckForUpdates.Id.Should().Be("application.check_for_updates");
        ApplicationUpgrade.Id.Should().Be("application.upgrade");
    }

    private sealed class Maintenance : IApplicationMaintenance
    {
        internal int Calls;
        internal ReleaseVersion? Version;
        internal RequestOrigin Origin;
        internal Func<bool>? Admitted;
        internal CancellationToken Token;
        public Task<string> CheckForUpdatesAsync(ReleaseVersion? version, RequestOrigin origin,
            Func<bool> admitted, CancellationToken cancellationToken) => Record(version, origin, admitted, false, cancellationToken);
        public Task<string> RequestUpgradeAsync(ReleaseVersion? version, RequestOrigin origin,
            Func<bool> admitted, CancellationToken cancellationToken) => Record(version, origin, admitted, true, cancellationToken);
        private Task<string> Record(ReleaseVersion? version, RequestOrigin origin,
            Func<bool> admitted, bool upgrade, CancellationToken token)
        {
            Calls++;
            Version = version;
            Origin = origin;
            Admitted = admitted;
            Token = token;
            return Task.FromResult(upgrade ? "native review required" : "checked");
        }
    }

    private sealed class McpMaintenance : IApplicationMaintenance, IApplicationMaintenanceMcp
    {
        public string? Id { get; private set; }
        public string? Input { get; private set; }
        public Func<bool>? Admitted { get; private set; }
        public CancellationToken Token { get; private set; }
        public ValueTask<CapabilityReply> InvokeAsync(string id, string input, Func<bool> admitted, CancellationToken token)
        {
            Id = id; Input = input; Admitted = admitted; Token = token;
            return ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Denied, "recorded"));
        }
        public Task<string> CheckForUpdatesAsync(ReleaseVersion? version, RequestOrigin origin, Func<bool> admitted, CancellationToken token) =>
            throw new InvalidOperationException("MCP must use its shared runner.");
        public Task<string> RequestUpgradeAsync(ReleaseVersion? version, RequestOrigin origin, Func<bool> admitted, CancellationToken token) =>
            throw new InvalidOperationException("MCP must use its shared runner.");
    }
}
