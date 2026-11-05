using AwesomeAssertions;

using Kora.Setup;

using WixToolset.BootstrapperApplicationApi;

namespace Kora.Windows.IntegrationTests;

public sealed class InstallerRelatedBundleTests
{
    [Theory]
    [InlineData(LaunchAction.Uninstall, Display.None, RelationType.Upgrade, true)]
    [InlineData(LaunchAction.Uninstall, Display.Embedded, RelationType.Upgrade, true)]
    [InlineData(LaunchAction.Uninstall, Display.None, RelationType.None, false)]
    [InlineData(LaunchAction.Uninstall, Display.None, RelationType.Update, false)]
    [InlineData(LaunchAction.Install, Display.None, RelationType.Upgrade, false)]
    [InlineData(LaunchAction.Repair, Display.Embedded, RelationType.Upgrade, false)]
    [InlineData(LaunchAction.Uninstall, Display.Passive, RelationType.Upgrade, false)]
    [InlineData(LaunchAction.Uninstall, Display.Full, RelationType.Upgrade, false)]
    public void OnlyBurnRelatedUpgradeUninstallCanRunWithoutInteractiveSetup(
        LaunchAction action, Display display, RelationType relation, bool allowed)
    {
        InstallerRelatedBundlePolicy.IsRelatedUninstall(action, display, relation).Should().Be(allowed);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Kora.Installer.v1", false)]
    [InlineData(InstallerRelatedBundlePolicy.ProtocolTag, true)]
    public void ImmutableOrUncachedBundlesRequireExplicitMigrationBeforeMutation(string tag, bool missing)
    {
        var admit = () => InstallerRelatedBundlePolicy.ValidateUpgrade(tag, missing, "0.1.1", "0.1.2");
        admit.Should().Throw<InvalidDataException>().WithMessage("*original full-display setup*No package changes*");
    }

    [Fact]
    public void CompatibleCachedBundleCanBeRetiredByBurn()
    {
        InstallerRelatedBundlePolicy.ValidateUpgrade(InstallerRelatedBundlePolicy.ProtocolTag, false, "0.1.1.0", "0.1.2");
    }

    [Theory]
    [InlineData("0.1.2")]
    [InlineData("0.1.2.0")]
    [InlineData("0.1.3")]
    [InlineData("0.1.1.1")]
    [InlineData("0.1")]
    [InlineData("unproven")]
    public void SameNewerOrUnknownRelatedVersionCannotAdmitReplacement(string related)
    {
        var admit = () => InstallerRelatedBundlePolicy.ValidateUpgrade(
            InstallerRelatedBundlePolicy.ProtocolTag, false, related, "0.1.2");
        admit.Should().Throw<InvalidDataException>();
    }
}
