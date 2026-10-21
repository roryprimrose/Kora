using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>One exact retained slot observation for original-user native inspection, never a catalogue query or capture grant.</summary>
public interface ISessionAttachmentInspectionStore
{
    /// <summary>Observes <paramref name="session"/> under <paramref name="admitted"/> and <paramref name="token"/> without opening source bytes.</summary>
    ValueTask<SessionAttachmentSummary?> ReadAttachmentInspection(HostId<SessionIdentity> session,
        Func<bool> admitted, CancellationToken token);
}
