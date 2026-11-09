using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora;

internal abstract record SessionDocumentTab(DetailContentReference Reference, string Title,
    HostId<SessionIdentity>? Session);
