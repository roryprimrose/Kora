using Kora.Core.Skills;

namespace Kora.Definitions.Skills;

public static class EmbeddedFixedActionCatalogue
{
    public static BundledFixedScript Resolve(SkillPackageSnapshot package)
    {
        ArgumentNullException.ThrowIfNull(package);
        // These host-selected registrations bind the entire manifest/resource map and original bytes.
        // A definition's action string or declared dependency cannot register a host effect.
        var (action, digest) = package.Manifest.Id switch
        {
            "kora.session.lock" => (FixedSessionAction.Lock,
                "d93d9e99fb9c6260295b757a60fa80c98b6098af60e7f588d6c20bc13156497b"),
            "kora.computer.shutdown" => (FixedSessionAction.Shutdown,
                "7f5237b43dfc9ebd761e3b5e148ee1764f8eef6afbc48b5070985168fa90daed"),
            "kora.computer.restart" => (FixedSessionAction.Restart,
                "5a83b4286585bf3c478929bed4c6cd5e8fc20ff621ebb0d2c3439058d06a235d"),
            _ => throw new InvalidDataException("This bundled package has no host-registered fixed effect."),
        };
        if (!string.Equals(package.DeclaredResourceDigest, digest, StringComparison.Ordinal))
        { throw new InvalidDataException("The fixed effect registration does not match the complete immutable bundled package."); }
        return new(package, action);
    }
}