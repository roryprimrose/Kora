using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.KnowledgeSources;

/// <summary>Session-owned metadata only; never source content, read permission or model consent.</summary>
public sealed record SessionKnowledgeSourceBinding
{
    public SessionKnowledgeSourceBinding(Guid bindingId, Guid profileId, HostId<SessionIdentity> session,
        HostRevision generation, HostRevision storageRevision, KnowledgeSourceReference source, long sourceVersion)
    {
        ArgumentNullException.ThrowIfNull(source);
        session.Validate();
        if (bindingId == Guid.Empty || profileId == Guid.Empty || generation.Value <= 0
            || storageRevision.Value <= 0 || source.SourceId == Guid.Empty || source.RevisionId == Guid.Empty
            || sourceVersion <= 0)
        { throw new InvalidDataException("A binding requires exact profile, session, generation and registered revision metadata."); }
        BindingId = bindingId;
        ProfileId = profileId;
        Session = session;
        Generation = generation;
        StorageRevision = storageRevision;
        Source = source;
        SourceVersion = sourceVersion;
    }

    public Guid BindingId { get; }
    public Guid ProfileId { get; }
    public HostId<SessionIdentity> Session { get; }
    public HostRevision Generation { get; }
    public HostRevision StorageRevision { get; }
    public KnowledgeSourceReference Source { get; }
    public long SourceVersion { get; }

    /// <summary>Compares exact observed metadata; a match does not grant source access or renew any authority.</summary>
    public bool Matches(SessionWorkspaceEntry target, KnowledgeSource source, KnowledgeSourceReference revision)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(revision);
        return target.Authority.IsActive && target.Authority.SessionId == Session
            && target.Authority.Generation == Generation && source.State == KnowledgeSourceState.Enabled
            && source.Id == Source.SourceId && source.Version == SourceVersion && revision == Source;
    }
}
