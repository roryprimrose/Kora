using System.Globalization;
using Kora.Core.Utilities;

namespace Kora.Tools.Utilities;

public sealed class UuidInspect
{
    public string Execute(string canonical)
    {
        var value = UtilityUuid.Parse(canonical);
        return "Standalone utility UUID: " + value.Canonical
            + "\nCanonical lowercase D format; version nibble " + value.VersionNibble.ToString(CultureInfo.InvariantCulture)
            + "; " + (value.IsRfcVariant ? "RFC variant" : "unknown variant") + ".\n" + UuidHelp.Disclaimer;
    }
}
