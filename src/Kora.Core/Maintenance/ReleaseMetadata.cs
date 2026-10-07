namespace Kora.Core.Maintenance;

public sealed record ReleaseMetadata(long Id, ReleaseVersion Version, string SourceRevision,
    ReleaseArchitecture Architecture, IReadOnlyList<ReleaseAsset> Assets)
{
    public Uri Page => CanonicalRelease.Page(Version);
    public string ArtifactName => $"Kora-{Version.ToString()}-{CanonicalRelease.Rid(Architecture)}.zip";
    public string ArchitectureDisclosure => Architecture == ReleaseArchitecture.X86
        ? "x86 application ZIP only; no x86 MSI/Burn. Native speech/inference capability acceptance remains unavailable."
        : "x64 application ZIP and manual MSI/Burn; installed/native/protected-deployment acceptance remains outstanding.";
}
