namespace Kora.Core.Context;

public sealed record ClipboardSnapshotStatusResult
{
    private ClipboardSnapshotStatusResult(Guid? snapshotId, int? utf8Bytes)
    {
        SnapshotId = snapshotId;
        Utf8Bytes = utf8Bytes;
    }

    public Guid? SnapshotId { get; }
    public int? Utf8Bytes { get; }
    public bool IsPresent => SnapshotId is not null;
    public static ClipboardSnapshotStatusResult Unavailable { get; } = new(null, null);

    public static ClipboardSnapshotStatusResult FromCurrent(Guid requestedId, ClipboardSnapshot? current) =>
        current is not null && current.SnapshotId == requestedId
            ? new(current.SnapshotId, current.Utf8Bytes) : Unavailable;
}
