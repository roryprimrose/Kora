namespace Kora.Core.Maintenance;

public sealed record ReleaseMetadata(long Id, ReleaseVersion Version, string SourceRevision,
    ReleaseArchitecture Architecture, IReadOnlyList<ReleaseAsset> Assets)
{
    public int InstallerProtocol { get; init; }
    public bool InstallerUpgradeSupported => Architecture == ReleaseArchitecture.X64
        && InstallerProtocol == CanonicalRelease.InstallerProtocol;
    public Uri Page => CanonicalRelease.Page(Version);
    public string ArtifactName => $"Kora-{Version.ToString()}-{CanonicalRelease.Rid(Architecture)}.zip";
    public string ArchitectureDisclosure => Architecture == ReleaseArchitecture.X86
        ? "x86 application ZIP only; no x86 MSI/Burn. Native speech/inference capability acceptance remains unavailable."
        : "x64 application ZIP and reviewed MSI/Burn installer; installed/native/protected-deployment acceptance remains outstanding.";
}
