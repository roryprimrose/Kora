namespace Kora.Windows.Skills.RuntimeSetup;

public sealed record BundledRuntimeInstallation(
    string Executable, string Version, string InventoryDigest, string DeviceDigest,
    IReadOnlyList<BundledRuntimeFile> Files, string License, string ThirdPartyNotices)
{
    public long Bytes => Files.Sum(file => file.Bytes);
}
