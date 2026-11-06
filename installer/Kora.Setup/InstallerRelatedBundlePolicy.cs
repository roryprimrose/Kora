using WixToolset.BootstrapperApplicationApi;

namespace Kora.Setup;

public static class InstallerRelatedBundlePolicy
{
    public const string ProtocolTag = "Kora.Installer.v2";

    public static bool IsRelatedUninstall(LaunchAction action, Display display, RelationType relation)
        => action == LaunchAction.Uninstall && display is (Display.None or Display.Embedded) && relation == RelationType.Upgrade;

    public static void ValidateUpgrade(string tag, bool missingFromCache, string relatedVersion, string candidateVersion)
    {
        if (!string.Equals(tag, ProtocolTag, StringComparison.Ordinal) || missingFromCache)
        {
            throw new InvalidDataException(
                "The installed bundle cannot participate in this upgrade protocol. Close Kora, remove the old " +
                "release using its original full-display setup or Windows Installed apps, then run this installer. " +
                "Your device-local data is retained. No package changes were admitted.");
        }
        if (NumericVersion(relatedVersion) >= NumericVersion(candidateVersion))
        {
            throw new InvalidDataException(
                "The installed related bundle is the same version or newer. Use its original setup to repair it; no downgrade or same-version replacement was admitted.");
        }
    }

    private static Version NumericVersion(string text)
    {
        if (!Version.TryParse(text, out var version) || version.Build < 0 ||
            version.Revision is not (-1 or 0) || version.Major > 255 || version.Minor > 255 || version.Build > 65535)
        {
            throw new InvalidDataException("The related bundle has an unsupported numeric version.");
        }
        return new Version(version.Major, version.Minor, version.Build);
    }
}
