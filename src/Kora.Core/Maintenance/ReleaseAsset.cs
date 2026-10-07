namespace Kora.Core.Maintenance;

public sealed record ReleaseAsset(long Id, string Name, long Bytes, string Sha256);
