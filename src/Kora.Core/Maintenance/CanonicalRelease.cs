namespace Kora.Core.Maintenance;

public static class CanonicalRelease
{
    public const string Repository = "roryprimrose/Kora";
    public const string RepositoryUrl = "https://github.com/" + Repository;
    public const string ApiRoot = "https://api.github.com/repos/" + Repository;
    public const string Runtime = "framework-dependent .NET 10 + Windows Desktop 10 + native prerequisites";
    public const string Limits = "Unsigned POC; hashes detect consistency, not publisher authentication. "
        + "No updater, download, staging, execution, activation or source changes. Deployment mode is unknown.";

    public static Uri Page(ReleaseVersion version) => new(RepositoryUrl + "/releases/tag/v" + version.ToString());
    public static string Rid(ReleaseArchitecture architecture) => architecture switch
    {
        ReleaseArchitecture.X64 => "win-x64",
        ReleaseArchitecture.X86 => "win-x86",
        _ => throw new InvalidDataException("No published application artifact is admitted for this architecture."),
    };

    public static IReadOnlyList<string> AssetNames(ReleaseVersion version) =>
    [
        $"Kora-{version}-win-x64.zip", $"Kora-{version}-win-x86.zip",
        $"Kora-{version}-win-x64.msi", $"Kora-Setup-{version}-win-x64.exe",
        "installer-build.json", "payload-manifest.json", "SHA256SUMS.txt", "release-manifest.json",
        $"Kora-{version}-source-tools.zip",
    ];
}
