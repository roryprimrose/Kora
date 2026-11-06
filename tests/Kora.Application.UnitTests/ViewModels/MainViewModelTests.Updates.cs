using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Tools.Application;
using Kora.Core.Auditing;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("maintenance check", false, null)]
    [InlineData("Kora, maintenance upgrade 1.2.3", true, "1.2.3")]
    public async Task Update_commands_use_the_registered_actions_without_reasoning_or_installer_approval(string text, bool upgrade, string? version)
    {
        var f = await Fixture.CreateInitializedAsync();
        var host = new MaintenanceUpdates();
        f.ViewModel.BindMaintenanceUpdateTools(new ApplicationCheckForUpdates(host), new ApplicationUpgrade(host));
        var reviews = 0;
        f.ViewModel.MaintenanceRequested += (_, _) => reviews++;
        f.TextToSpeech.ClearSpokenResponse();
        await f.RunAsync(text);
        host.Calls.Should().Be(1);
        host.Version?.ToString().Should().Be(version);
        host.Origin.Should().Be(RequestOrigin.LocalUi);
        host.Upgrade.Should().Be(upgrade);
        f.ViewModel.ResponseBody.Should().Be("Fresh check; native review required for installation.");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        reviews.Should().Be(upgrade ? 1 : 0);
    }

    [Fact]
    public async Task An_unavailable_update_action_is_explicit_and_never_falls_back_to_the_model()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("maintenance check");
        f.ViewModel.ResponseBody.Should().Contain("No check or installer was started");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("maintenance upgrade https://example.com/installer.exe")]
    [InlineData("maintenance check 1.2.3 extra")]
    public async Task Invalid_update_targets_never_reach_actions_or_models(string text)
    {
        var f = await Fixture.CreateInitializedAsync();
        var host = new MaintenanceUpdates();
        f.ViewModel.BindMaintenanceUpdateTools(new ApplicationCheckForUpdates(host), new ApplicationUpgrade(host));
        await f.RunAsync(text);
        host.Calls.Should().Be(0);
        f.ViewModel.ResponseBody.Should().Contain(MaintenanceUpdateCommand.Syntax);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Activated_update_check_preserves_original_voice_and_never_grants_installer_authority()
    {
        var f = await Fixture.CreateInitializedAsync();
        var host = new MaintenanceUpdates();
        f.ViewModel.BindMaintenanceUpdateTools(new ApplicationCheckForUpdates(host), new ApplicationUpgrade(host));
        await f.RaiseActivatedTranscriptAsync("Kora, maintenance check", 1);
        host.Calls.Should().Be(1);
        host.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("io")]
    [InlineData("invalid")]
    [InlineData("unauthorized")]
    [InlineData("cancel")]
    [InlineData("network")]
    [InlineData("state")]
    [InlineData("call")]
    [InlineData("privacy")]
    [InlineData("topology")]
    [InlineData("disposed")]
    public async Task Failed_and_late_update_operations_do_not_present_success_or_retry(string failure)
    {
        var f = await Fixture.CreateInitializedAsync();
        var host = new MaintenanceUpdates
        {
            BeforeReturn = () =>
            {
                switch (failure)
                {
                    case "io": throw new IOException("fixture");
                    case "invalid": throw new InvalidDataException("fixture");
                    case "unauthorized": throw new UnauthorizedAccessException("fixture");
                    case "cancel": throw new OperationCanceledException("fixture");
                    case "network": throw new HttpRequestException("fixture");
                    case "state": throw new InvalidOperationException("fixture");
                    case "call": f.CallState.SetState(Kora.Core.Communication.CallState.Active); break;
                    case "privacy": f.Session.IsUnlocked = false; break;
                    case "topology": f.PrivacyObservation.Current = f.PrivacyObservation.Current with
                        { TopologyRevision = f.PrivacyObservation.Current.TopologyRevision + 1 }; break;
                    case "disposed": f.ViewModel.Dispose(); break;
                }
            },
        };
        f.ViewModel.BindMaintenanceUpdateTools(new ApplicationCheckForUpdates(host), new ApplicationUpgrade(host));
        await f.RunAsync("maintenance check");
        host.Calls.Should().Be(1);
        f.ViewModel.ResponseBody.Should().NotBe("Fresh check; native review required for installation.");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.System)]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    public async Task Synthetic_update_origins_cannot_be_promoted_to_original_native_authority(SecurityAuditInitiator initiator)
    {
        var f = await Fixture.CreateInitializedAsync();
        var host = new MaintenanceUpdates();
        f.ViewModel.BindMaintenanceUpdateTools(new ApplicationCheckForUpdates(host), new ApplicationUpgrade(host));
        await f.ViewModel.ExecuteMaintenanceUpdateCommandAsync(new(false, null), initiator);
        host.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Disposed_update_host_and_unbound_actions_do_not_dispatch()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.Dispose();
        await f.ViewModel.ExecuteMaintenanceUpdateCommandAsync(new(false, null), SecurityAuditInitiator.LocalUser);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    private sealed class MaintenanceUpdates : IApplicationMaintenance
    {
        internal int Calls;
        internal ReleaseVersion? Version;
        internal RequestOrigin Origin;
        internal bool Upgrade;
        internal Action? BeforeReturn;
        public Task<string> CheckForUpdatesAsync(ReleaseVersion? version, RequestOrigin origin,
            Func<bool> admitted, CancellationToken cancellationToken) => Execute(version, origin, admitted, false);
        public Task<string> RequestUpgradeAsync(ReleaseVersion? version, RequestOrigin origin,
            Func<bool> admitted, CancellationToken cancellationToken) => Execute(version, origin, admitted, true);
        private Task<string> Execute(ReleaseVersion? version, RequestOrigin origin, Func<bool> admitted, bool upgrade)
        {
            admitted().Should().BeTrue();
            Calls++;
            Version = version;
            Origin = origin;
            Upgrade = upgrade;
            BeforeReturn?.Invoke();
            return Task.FromResult("Fresh check; native review required for installation.");
        }
    }
}
