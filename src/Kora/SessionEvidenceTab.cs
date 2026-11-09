using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora;

internal sealed record SessionEvidenceTab(DetailContentReference Reference, string Title,
    HostId<SessionIdentity> EvidenceSession)
    : SessionDocumentTab(Reference, Title, EvidenceSession);
