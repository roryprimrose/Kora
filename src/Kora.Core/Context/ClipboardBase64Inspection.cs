namespace Kora.Core.Context;

public abstract record ClipboardBase64Inspection
{
    private ClipboardBase64Inspection() { }

    public sealed record AdmittedProfile(int DecodedByteCount) : ClipboardBase64Inspection;
    public sealed record InvalidProfile(ClipboardBase64InvalidReason Reason, int Utf16Offset) : ClipboardBase64Inspection;
}
