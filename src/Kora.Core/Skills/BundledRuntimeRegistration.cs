namespace Kora.Core.Skills;

/// <summary>A device-local inventory pin, never execution or containment authority.</summary>
public sealed record BundledRuntimeRegistration
{
    public BundledRuntimeRegistration(string inventoryDigest, string deviceDigest, bool confirmed)
    {
        ValidateDigest(inventoryDigest);
        ValidateDigest(deviceDigest);
        InventoryDigest = inventoryDigest;
        DeviceDigest = deviceDigest;
        Confirmed = confirmed;
    }

    public string InventoryDigest { get; }
    public string DeviceDigest { get; }
    public bool Confirmed { get; }

    public string Serialize() => $"kora-bundled-runtime-v1\n{InventoryDigest}\n{DeviceDigest}\n{(Confirmed ? "confirmed" : "unconfirmed")}";

    public static BundledRuntimeRegistration Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var fields = value.Split('\n');
        if (fields.Length != 4 || !string.Equals(fields[0], "kora-bundled-runtime-v1", StringComparison.Ordinal)
            || fields[3] is not ("confirmed" or "unconfirmed"))
        { throw new InvalidDataException("The saved bundled runtime registration is invalid. Restore or explicitly remove the local registration; no runtime was selected."); }
        return new(fields[1], fields[2], string.Equals(fields[3], "confirmed", StringComparison.Ordinal));
    }

    private static void ValidateDigest(string value)
    {
        if (value is null || value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        { throw new InvalidDataException("Runtime registration identities must be exact lowercase SHA-256 digests."); }
    }
}
