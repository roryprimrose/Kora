namespace Kora.Windows.Coordination;

/// <summary>
/// One non-sensitive dirty bit survives disappearance of the last named-mutex handle.
/// It contains no identity, ticket, conversation, approval or work/session state.
/// </summary>
internal sealed class OwnerContinuity : IDisposable
{
    private readonly string path;
    private readonly string sid;
    private FileStream? marker;
#if DEBUG
    internal DebugOwnerRecovery DebugRecovery { get; }
#endif

    internal OwnerContinuity(string sid, string? markerPath = null)
    {
        this.sid = sid;
        path = markerPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kora", "Coordination", "unclean-owner");
#if DEBUG
        DebugRecovery = new(path, sid, DebugOwnerRecovery.CurrentOwner());
#endif
    }

#if DEBUG
    internal bool HasUncleanOwner => !DebugRecovery.CanStart;
#else
    internal bool HasUncleanOwner => File.Exists(path);
#endif

    internal void Begin()
    {
#if DEBUG
        DebugRecovery.Prepare();
#endif
        if (File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The previous assistant has no verified clean exit. Reconcile possible owned workers/" +
                $"uncertain effects, then explicitly remove the non-sensitive marker '{path}' and launch manually.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        marker = CoordinationNative.CreateUserContinuityFile(path, sid);
        marker.WriteByte(1);
        marker.Flush(flushToDisk: true);
#if DEBUG
        DebugRecovery.Begin();
#endif
    }

    internal void End(bool safelyDisposed)
    {
        if (marker is null)
        {
            return;
        }

        marker.Dispose();
        marker = null;
#if DEBUG
        DebugRecovery.End(safelyDisposed);
#endif
        if (safelyDisposed)
        {
            File.Delete(path);
        }
    }

    public void Dispose() => End(safelyDisposed: false);
}
