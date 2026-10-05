namespace Kora.Core.Coordination;

public sealed record InstanceBuildIdentity(
    string Version,
    string BuildId,
    string ContentDigest,
    bool IsDebug,
    string ExecutablePath,
    string DeploymentIdentity)
{
    public bool HasValidContentDigest =>
        ContentDigest.Length == 64 && ContentDigest.All(char.IsAsciiHexDigit);

    public bool IsSameBuild(InstanceBuildIdentity other) =>
        HasValidContentDigest && other.HasValidContentDigest &&
        IsDebug == other.IsDebug &&
        string.Equals(ContentDigest, other.ContentDigest, StringComparison.Ordinal);

    public string DisplayName =>
        $"Kora {Version}, {(IsDebug ? "Debug" : "Release")}, build {BuildId}\n" +
        $"{ExecutablePath}\nDeployment: {DeploymentIdentity}\nSHA-256: {ContentDigest}";
}
