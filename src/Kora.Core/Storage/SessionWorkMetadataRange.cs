using System.Runtime.InteropServices;

namespace Kora.Core.Storage;

/// <summary>Inclusive retained work metadata bounds; absent endpoints impose no restriction.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SessionWorkMetadataRange(long? Minimum = null, long? Maximum = null)
{
    public void Validate(bool positive)
    {
        var floor = positive ? 1 : 0;
        if (Minimum < floor || Maximum < floor || Minimum > Maximum)
        {
            throw new InvalidDataException("Work metadata ranges require ordered "
                + (positive ? "positive" : "nonnegative") + " Int64 bounds. Empty means Any.");
        }
    }

    public bool Contains(long value) =>
        (Minimum is not { } minimum || value >= minimum)
        && (Maximum is not { } maximum || value <= maximum);

    public static SessionWorkMetadataRange Parse(string minimum, string maximum) =>
        new(OptionalUnsignedDecimal.Parse(minimum), OptionalUnsignedDecimal.Parse(maximum));
}
