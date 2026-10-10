using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Private historical single-file snapshots; never execution, filesystem or model authority.</summary>
public interface ISessionFileStore
{
    /// <summary>Reads the exact retained snapshot without changing activity or lifecycle.</summary>
    ValueTask<SessionFileAttachment?> ReadAttachment(HostId<SessionIdentity> session, CancellationToken token);

    /// <summary>Commits only bytes captured under the same exact original-user native review.</summary>
    ValueTask<SessionFileAttachment> Attach(HostRequest request, HostRevision generation, LocalFileRevision revision,
        ReadOnlyMemory<byte> originalBytes, Func<bool> admitted, CancellationToken token);

    /// <summary>Atomically swaps the exact reviewed retained slot; disclosure waits for old-copy verification.</summary>
    ValueTask<SessionFileAttachment> Replace(HostRequest request, SessionFileRemoval previous, LocalFileRevision revision,
        ReadOnlyMemory<byte> originalBytes, Func<bool> admitted, CancellationToken token);

    /// <summary>Reviews the exact body, revision and complete owned-copy inventory before removal.</summary>
    ValueTask<SessionFileRemoval> PreviewRemoval(HostId<SessionIdentity> session, CancellationToken token);

    /// <summary>Revokes before secure deletion and reports completion only after owned-copy verification.</summary>
    ValueTask Remove(HostRequest request, SessionFileRemoval review, Func<bool> admitted, CancellationToken token);
}
