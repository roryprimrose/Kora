using Kora.Core.Utilities;

namespace Kora.Tools.Utilities;

public sealed class UuidGenerate(IUtilityUuidGenerator generator)
{
    public string Execute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = UtilityUuid.Parse(generator.Generate().ToString("D"));
        cancellationToken.ThrowIfCancellationRequested();
        if (value.VersionNibble != 4 || !value.IsRfcVariant)
        { throw new InvalidDataException("UUID generator returned an invalid version 4 RFC variant UUID."); }
        return $"Standalone utility UUID: {value.Canonical}\nversion nibble 4; RFC variant.\n{UuidHelp.Disclaimer}";
    }
}
