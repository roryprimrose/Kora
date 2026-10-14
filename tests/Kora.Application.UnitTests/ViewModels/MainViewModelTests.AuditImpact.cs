using AwesomeAssertions;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Native_explicit_selection_exact_status_refresh_edit_close_and_dispose_are_read_only()
    {
        var unavailable = new Fixture();
        unavailable.ViewModel.AuditImpactPreviewStatus.Should().Contain("unavailable");
        unavailable.ViewModel.CanPreviewAuditImpact.Should().BeFalse();
        unavailable.ViewModel.ProposedAuditImpactDays = 30;
        unavailable.ViewModel.ProposedAuditImpactDays = null;
        var fixture = new Fixture(enableAuditRetention: true, enableAuditImpact: true);
        await using var admission = fixture.AuditAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindAuditRetentionNativeLifetime(static () => true);
        fixture.ViewModel.ProposedAuditImpactDays.Should().BeNull();
        fixture.ViewModel.CanPreviewAuditImpact.Should().BeFalse();
        fixture.ViewModel.ProposedAuditImpactDays = 90;
        fixture.ViewModel.CanPreviewAuditImpact.Should().BeFalse();
        fixture.ViewModel.ProposedAuditImpactDays = 30;
        fixture.ViewModel.ProposedAuditImpactDays = 30;
        fixture.ViewModel.CanPreviewAuditImpact.Should().BeTrue();
        await fixture.ViewModel.PreviewAuditImpactCommand.ExecuteAsync();
        fixture.ViewModel.AuditImpactPreviewStatus.Should().Contain("1 newly due").And.Contain("0 newly due")
            .And.Contain("Required-authority").And.Contain("Diagnostic-audit").And.Contain("Committed UTC").And.Contain("Proposed due UTC");
        fixture.AuditPreferences.Value.Should().BeNull();
        fixture.ViewModel.ProposedAuditImpactDays = 31;
        fixture.ViewModel.ProposedAuditImpactDays = null;
        fixture.ViewModel.ProposedAuditImpactDays = 31;
        fixture.ViewModel.AuditImpactPreviewStatus.Should().Contain("Proposal edited");
        fixture.ImpactReader.ProjectionStatus = AuditImpactStatus.Incomplete;
        await fixture.ViewModel.PreviewAuditImpactCommand.ExecuteAsync();
        fixture.ViewModel.AuditImpactPreviewStatus.Should().Contain("Incomplete; no count");
        await fixture.ViewModel.RefreshAuditRetentionCommand.ExecuteAsync();
        fixture.ViewModel.AuditImpactPreviewStatus.Should().Contain("retired");
        await fixture.ViewModel.PreviewAuditImpactCommand.ExecuteAsync();
        fixture.ViewModel.BindAuditRetentionNativeLifetime(static () => false);
        fixture.ViewModel.AuditImpactPreviewStatus.Should().Contain("retired");
        fixture.ViewModel.BindAuditRetentionNativeLifetime(static () => true);
        fixture.ImpactReader.Current = false;
        await fixture.ViewModel.PreviewAuditImpactCommand.ExecuteAsync();
        fixture.ViewModel.AuditImpactPreviewStatus.Should().Contain("failed");
        fixture.ViewModel.Dispose();
        fixture.ImpactReader.Signal();
        unavailable.ViewModel.Dispose();
    }

    [Fact]
    public async Task Call_owner_private_and_pending_control_changes_retire_published_observation()
    {
        var fixture = new Fixture(enableAuditRetention: true, enableAuditImpact: true);
        await using var admission = fixture.AuditAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindAuditRetentionNativeLifetime(static () => true);
        fixture.ViewModel.ProposedAuditImpactDays = 30;
        await fixture.ViewModel.PreviewAuditImpactCommand.ExecuteAsync();
        await fixture.ViewModel.SetManualCallAsync(true, Kora.Core.Hosting.RequestOrigin.LocalUi, fixture.ViewModel.CallPolicyRevision,
            TestContext.Current.CancellationToken);
        fixture.ViewModel.AuditImpactPreviewStatus.Should().Contain("retired");
        var current = fixture.PrivacyObservation.Current;
        fixture.PrivacyObservation.Publish(new(current, current with { TopologyRevision = current.TopologyRevision + 1 },
            Kora.Core.Platform.WindowsPrivacyChangeReason.DeviceTopology));
        await fixture.ViewModel.PreviewAuditImpactCommand.ExecuteAsync();
        var ownerRevision = 1L;
        fixture.ViewModel.BindCallOwnershipGate(static () => true, () => ownerRevision);
        await fixture.ViewModel.PreviewAuditImpactCommand.ExecuteAsync();
        ownerRevision++;
        typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("RetireIneligibleAuditImpact",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(fixture.ViewModel, []);
        fixture.ViewModel.AuditImpactPreviewStatus.Should().Contain("retired");
        fixture.ViewModel.BindCallOwnershipGate(static () => false);
        fixture.ViewModel.CanPreviewAuditImpact.Should().BeFalse();
        fixture.ViewModel.Dispose();
    }
}
