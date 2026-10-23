namespace Kora.Core.Dependencies;

public sealed record SpeechComponentAsset
{
    public SpeechComponentAsset(string name, Uri source, long sizeBytes, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        ArgumentNullException.ThrowIfNull(sha256);
        var parts = name.Split('.');
        if (parts.Length != 2 || parts[0].Length > 64
            || parts[0].Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            || parts[0].Length == 0
            || parts[0] is "con" or "prn" or "aux" or "nul"
            || parts[0] is "com1" or "com2" or "com3" or "com4" or "com5" or "com6" or "com7" or "com8" or "com9"
            || parts[0] is "lpt1" or "lpt2" or "lpt3" or "lpt4" or "lpt5" or "lpt6" or "lpt7" or "lpt8" or "lpt9"
            || parts[1] is not ("bin" or "onnx" or "json" or "txt" or "model" or "data"))
        {
            throw new ArgumentException("Only canonical flat model-data file names are admitted.", nameof(name));
        }
        if (!source.IsAbsoluteUri || !string.Equals(source.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || !source.IsDefaultPort
            || source.IsLoopback || source.HostNameType != UriHostNameType.Dns
            || source.UserInfo.Length != 0 || source.Query.Length != 0 || source.Fragment.Length != 0)
        {
            throw new ArgumentException("A component source must be an exact public HTTPS data URI.", nameof(source));
        }
        if (sha256.Length != 64 || sha256.Any(character => character is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
        {
            throw new ArgumentException("An exact uppercase SHA-256 digest is required.", nameof(sha256));
        }
        Name = name;
        Source = source;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
    }

    public string Name { get; }
    public Uri Source { get; }
    public long SizeBytes { get; }
    public string Sha256 { get; }
}
