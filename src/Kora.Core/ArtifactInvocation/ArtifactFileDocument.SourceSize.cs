using System.Text;

namespace Kora.Core.Artifacts;

public sealed partial class ArtifactFileDocument
{
    public static SourceBufferSize MeasureSourceBuffer(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int byteCount;
        try { byteCount = Utf8.GetByteCount(text); }
        catch (EncoderFallbackException) { return new(text.Length, null, null); }

        var scalars = 0;
        foreach (var rune in text.EnumerateRunes()) { scalars++; }
        return new(text.Length, byteCount, scalars);
    }
}
