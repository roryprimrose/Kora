using AwesomeAssertions;

using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task ExplicitNativeProposalIsSeparateFromFuturePolicyAndRetiresOnEditRefreshClose()
    {
        var unavailable = new Fixture();
        unavailable.ViewModel.SessionRetentionPlanStatus.Should().Contain("unavailable");
        unavailable.ViewModel.CanPreviewSessionRetention.Should().BeFalse();
        unavailable.ViewModel.ProposedSessionArchiveDays = 1;
        unavailable.ViewModel.ProposedSessionDeleteDays = null;
        unavailable.ViewModel.Dispose();
        var fixture = new Fixture(enableSessionRetention: true, enableDiagnosticRetention: true, enableSessionRetentionPlanner: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.BindSessionRetentionNativeLifetime(static () => true);
        fixture.ViewModel.ProposedSessionArchiveDays.Should().BeNull();
        fixture.ViewModel.CanPreviewSessionRetention.Should().BeFalse();
        await fixture.ViewModel.RefreshSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.ProposedSessionArchiveDays = 2;
        fixture.ViewModel.ProposedSessionDeleteDays = 2;
        fixture.ViewModel.CanPreviewSessionRetention.Should().BeFalse();
        fixture.ViewModel.ProposedSessionDeleteDays = 3;
        fixture.ViewModel.CanPreviewSessionRetention.Should().BeTrue();
        await fixture.ViewModel.PreviewSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("Complete")
            .And.Contain("Fixed UTC cutoff").And.Contain("newly delete-due: 1")
            .And.Contain("not recomputed").And.Contain("unknown work hold")
            .And.Contain("Preference revision").And.Contain("private revision")
            .And.Contain("Exact metadata snapshot");
        fixture.SessionRetentionPreferences.Value.Should().BeNull();
        fixture.SessionRetentionConfiguration!.Settings.Should().Be(SessionRetentionSettings.Default);
        fixture.ViewModel.SelectedSessionArchiveDays.Should().Be(1);
        fixture.ViewModel.SelectedSessionDeleteDays.Should().Be(30);
        fixture.ViewModel.ProposedSessionDeleteDays = 4;
        fixture.ViewModel.ProposedSessionDeleteDays = 4;
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("edited");
        fixture.SessionPlanReader.Result = new(SessionRetentionPlanStatus.Incomplete, null, "stamp");
        await fixture.ViewModel.PreviewSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("Incomplete").And.Contain("No aggregate available");
        await fixture.ViewModel.RefreshSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("retired");
        fixture.SessionPlanReader.Result = new(SessionRetentionPlanStatus.Complete, new(0, 0, 0, 0, 0, 0, 0, 0, 0), "stamp");
        await fixture.ViewModel.PreviewSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("Sessions observed: 0");
        fixture.ViewModel.BindSessionRetentionNativeLifetime(static () => false);
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("retired");
        fixture.ViewModel.CanPreviewSessionRetention.Should().BeFalse();
        fixture.ViewModel.Dispose();
        fixture.SessionPlanReader.Signal();
    }

    [Theory]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("source")]
    public async Task PrivacyOwnershipCallAndSourceLossRetireNativeResultsWithoutReplay(string reason)
    {
        var fixture = new Fixture(enableSessionRetention: true, enableDiagnosticRetention: true, enableSessionRetentionPlanner: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.BindSessionRetentionNativeLifetime(static () => true);
        await fixture.ViewModel.RefreshSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.ProposedSessionArchiveDays = 1;
        fixture.ViewModel.ProposedSessionDeleteDays = 3;
        await fixture.ViewModel.PreviewSessionRetentionCommand.ExecuteAsync();
        if (reason == "private")
        {
            var current = fixture.PrivacyObservation.Current;
            fixture.PrivacyObservation.Publish(new(current, current with { TopologyRevision = current.TopologyRevision + 1 },
                Kora.Core.Platform.WindowsPrivacyChangeReason.DeviceTopology));
        }
        if (reason == "owner") { fixture.ViewModel.BindCallOwnershipGate(static () => false); }
        if (reason == "call")
        {
            await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, fixture.ViewModel.CallPolicyRevision,
                TestContext.Current.CancellationToken);
        }
        if (reason == "source") { fixture.SessionPlanReader.Signal(); }
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("retired");
        fixture.SessionPlanReader.Requests.Should().HaveCount(1);
        fixture.ViewModel.Dispose();
    }
}
