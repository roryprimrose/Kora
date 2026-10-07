namespace Kora.Windows.Context;

internal interface IClipboardNative
{
    uint GetSequence();
    bool Open();
    bool Close();
    bool HasUnicodeText();
    nint GetUnicodeText();
    nuint GetSize(nint handle);
    nint Lock(nint handle);
    bool Unlock(nint handle);
    char ReadCodeUnit(nint pointer, int index);
    int LastError { get; }
}
