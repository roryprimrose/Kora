namespace Kora.Windows.Skills.RuntimeSetup;

internal interface IBundledRuntimeNativeMetadata
{
    void RequireSupportedPlatform();
    string InspectProtection(string path, bool directory, bool ancestorOnly);
    string InspectExecutable(string path, FileStream input);
    string DeviceDigest();
}
