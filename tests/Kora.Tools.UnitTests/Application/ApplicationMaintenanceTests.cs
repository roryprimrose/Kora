using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Tools.Application;

namespace Kora.Tools.UnitTests.Application;

public sealed class ApplicationMaintenanceTests
{
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
}
