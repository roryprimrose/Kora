using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;

namespace Kora.Core.Storage;

/// <summary>Private, audited, one-slot metadata associations; never source copies or source-use authority.</summary>
public interface ISessionKnowledgeSourceBindingStore
{
    /// <summary>Reads only association metadata without source I/O, session activity or retention renewal.</summary>
    ValueTask<SessionKnowledgeSourceBinding?> ReadBinding(HostId<SessionIdentity> session, CancellationToken token);

    /// <summary>
    /// Commits an empty slot under exact original-user host admission and returns its durable audited readback.
    /// The caller must retain and revalidate the separately reviewed registered-source admission.
    /// </summary>
    ValueTask<SessionKnowledgeSourceBinding> Bind(HostRequest request, HostRevision generation,
        KnowledgeSourceReference source, long sourceVersion, Func<bool> admitted, CancellationToken token);

    /// <summary>Removes only the exact reviewed association, never the source, conversation or their retention policy.</summary>
    ValueTask Detach(HostRequest request, SessionKnowledgeSourceBinding exact, Func<bool> admitted, CancellationToken token);
}
