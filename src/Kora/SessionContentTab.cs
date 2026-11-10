using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora;

internal sealed record SessionContentTab(DetailContentReference Reference, string Title,
    HostId<SessionIdentity>? Session, Guid? RequestId, DetailContentOrigin Origin)
    : SessionDocumentTab(Reference, Title, Session);
