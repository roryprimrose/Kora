using System.Text;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Core.Context;

public sealed class ClipboardSnapshot
{
    public const int MaximumUtf8Bytes = NativeDetailProfile.MaximumUtf8Bytes;
    public const string Format = "CF_UNICODETEXT";
    private static readonly UTF8Encoding Encoding = new(false, true);

    public ClipboardSnapshot(Guid sourceId, Guid snapshotId, HostRequest request,
        string text, uint version, DateTimeOffset capturedAt)
    {
        if (sourceId == Guid.Empty || snapshotId == Guid.Empty || version == 0)
        {
            throw new InvalidDataException("Clipboard source, snapshot and read version must be host-resolved.");
        }
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || text.Contains('\0', StringComparison.Ordinal))
        {
            throw new InvalidDataException("Clipboard text must be nonempty, canonical Unicode plain text.");
        }
        var bytes = Encoding.GetByteCount(text);
        if (bytes > MaximumUtf8Bytes)
        {
            throw new InvalidDataException("Clipboard text exceeds the 256 KiB UTF-8 bound; no truncation is permitted.");
        }
        SourceId = sourceId;
        SnapshotId = snapshotId;
        Request = request;
        Text = text;
        Version = version;
        CapturedAt = capturedAt;
        Utf8Bytes = bytes;
    }

    public Guid SourceId { get; }
    public Guid SnapshotId { get; }
    public HostRequest Request { get; }
    public string Text { get; }
    public uint Version { get; }
    public DateTimeOffset CapturedAt { get; }
    public int Utf8Bytes { get; }
    public byte[] GetUtf8Bytes() => Encoding.GetBytes(Text);
}
