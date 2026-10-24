using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Dependencies;

/// <summary>Host-authored optional data only; native adapters and launch prerequisites are delivered with Kora.</summary>
public sealed class SpeechComponentDefinition
{
    public SpeechComponentDefinition(SpeechComponentIdentity identity, string name, string runtimeProfile,
        string modelIdentity, string licence, Uri licenceSource, string? qualification,
        long stagingBytes, long installedBytes, long ramBytes, long vramBytes,
        IEnumerable<SpeechComponentAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(licence);
        ArgumentNullException.ThrowIfNull(licenceSource);
        ArgumentNullException.ThrowIfNull(assets);
        SpeechComponentIdentity.ValidateToken(runtimeProfile);
        if (qualification is not null) { SpeechComponentIdentity.ValidateToken(qualification); }
        if (name.Length > 128 || modelIdentity.Length > 256 || licence.Length > 256
            || name.Any(char.IsControl) || modelIdentity.Any(char.IsControl) || licence.Any(char.IsControl)
            || !licenceSource.IsAbsoluteUri || !string.Equals(licenceSource.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || licenceSource.UserInfo.Length != 0 || licenceSource.Fragment.Length != 0)
        {
            throw new ArgumentException("Bounded display, exact model identity and HTTPS licence metadata are required.", nameof(name));
        }
        var files = assets.ToArray();
        if (files.Length is < 1 or > 16 || files.Any(asset => asset is null)
            || files.Select(asset => asset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
        {
            throw new ArgumentException("A component requires one to sixteen distinct data assets.", nameof(assets));
        }
        var download = files.Aggregate(0L, (total, asset) => checked(total + asset.SizeBytes));
        if (installedBytes < download || stagingBytes < installedBytes || ramBytes < 0 || vramBytes < 0)
        {
            throw new ArgumentException("Exact resource estimates must include all downloaded assets and staging.", nameof(stagingBytes));
        }
        Identity = identity;
        Name = name;
        RuntimeProfile = runtimeProfile;
        ModelIdentity = modelIdentity;
        Licence = licence;
        LicenceSource = licenceSource;
        Qualification = qualification;
        StagingBytes = stagingBytes;
        InstalledBytes = installedBytes;
        RamBytes = ramBytes;
        VramBytes = vramBytes;
        DownloadBytes = download;
        Assets = Array.AsReadOnly(files);
        // The ownership record binds the complete host offer, not just a mutable display identifier.
        var manifest = string.Join('\n', new[]
        {
            identity.ComponentId, identity.Revision, name, runtimeProfile, modelIdentity, licence,
            licenceSource.AbsoluteUri, qualification ?? string.Empty,
            stagingBytes.ToString(CultureInfo.InvariantCulture), installedBytes.ToString(CultureInfo.InvariantCulture),
            ramBytes.ToString(CultureInfo.InvariantCulture), vramBytes.ToString(CultureInfo.InvariantCulture),
        }.Concat(files.Select(asset => string.Join('|', asset.Name, asset.Source.AbsoluteUri,
            asset.SizeBytes.ToString(CultureInfo.InvariantCulture), asset.Sha256))));
        ManifestDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
    }

    public SpeechComponentIdentity Identity { get; }
    public string Name { get; }
    public string RuntimeProfile { get; }
    public string ModelIdentity { get; }
    public string Licence { get; }
    public Uri LicenceSource { get; }
    public string? Qualification { get; }
    public bool Qualified => Qualification is not null;
    public long DownloadBytes { get; }
    public long StagingBytes { get; }
    public long InstalledBytes { get; }
    public long RamBytes { get; }
    public long VramBytes { get; }
    public IReadOnlyList<SpeechComponentAsset> Assets { get; }
    public string ManifestDigest { get; }
}
