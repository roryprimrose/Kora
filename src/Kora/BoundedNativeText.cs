using System.Text;
using Kora.Core.Presentation;

namespace Kora;

internal sealed class BoundedNativeText
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly StringBuilder text = new();
    private int bytes;

    internal void Append(string? value)
    {
        if (value is null) { return; }
        var addedBytes = Utf8.GetByteCount(value);
        if (addedBytes > NativeDetailProfile.MaximumUtf8Bytes - bytes)
        {
            throw new InvalidDataException("Semantic expansion exceeds the native UTF-8 byte limit.");
        }
        bytes += addedBytes;
        text.Append(value);
    }

    public override string ToString() => text.ToString();
}
