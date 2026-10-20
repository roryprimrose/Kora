namespace Kora.Core.Context;

public enum ClipboardBase64InvalidReason
{
    EncodedSizeExceeded,
    EncodedLengthNotMultipleOfFour,
    InvalidAlphabet,
    InvalidPadding,
    NonCanonicalUnusedBits,
}
