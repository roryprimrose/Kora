namespace Kora.Core.Context;

public static class ClipboardBase64Policy
{
    public static ClipboardBase64Inspection Inspect(string encoded, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        token.ThrowIfCancellationRequested();
        if (encoded.Length > ClipboardSnapshot.MaximumUtf8Bytes)
        {
            return new ClipboardBase64Inspection.InvalidProfile(
                ClipboardBase64InvalidReason.EncodedSizeExceeded, ClipboardSnapshot.MaximumUtf8Bytes);
        }
        if (encoded.Length % 4 != 0)
        {
            return new ClipboardBase64Inspection.InvalidProfile(
                ClipboardBase64InvalidReason.EncodedLengthNotMultipleOfFour, encoded.Length);
        }

        var padding = 0;
        var lastValue = 0;
        for (var offset = 0; offset < encoded.Length; offset++)
        {
            token.ThrowIfCancellationRequested();
            var character = encoded[offset];
            if (character == '=')
            {
                if (offset < encoded.Length - 2)
                {
                    return new ClipboardBase64Inspection.InvalidProfile(
                        ClipboardBase64InvalidReason.InvalidPadding, offset);
                }
                padding++;
                continue;
            }
            if (padding != 0)
            {
                return new ClipboardBase64Inspection.InvalidProfile(
                    ClipboardBase64InvalidReason.InvalidPadding, offset);
            }
            var value = Sextet(character);
            if (value < 0)
            {
                return new ClipboardBase64Inspection.InvalidProfile(
                    ClipboardBase64InvalidReason.InvalidAlphabet, offset);
            }
            lastValue = value;
        }
        // Only the final two positions can be padding; unused low bits must be zero.
        if (padding != 0 && (lastValue & (padding == 2 ? 15 : 3)) != 0)
        {
            return new ClipboardBase64Inspection.InvalidProfile(
                ClipboardBase64InvalidReason.NonCanonicalUnusedBits, encoded.Length - padding - 1);
        }
        token.ThrowIfCancellationRequested();
        return new ClipboardBase64Inspection.AdmittedProfile(checked(encoded.Length / 4 * 3 - padding));
    }

    private static int Sextet(char character) => character switch
    {
        >= 'A' and <= 'Z' => character - 'A',
        >= 'a' and <= 'z' => character - 'a' + 26,
        >= '0' and <= '9' => character - '0' + 52,
        '+' => 62,
        '/' => 63,
        _ => -1,
    };
}
