using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private ISessionKnowledgeSourceBindingStore SourceBindings => store as ISessionKnowledgeSourceBindingStore
        ?? throw new InvalidOperationException("The private source association store is unavailable.");

    public Task<SessionKnowledgeSourceBinding?> ReadKnowledgeSourceBindingAsync(HostId<SessionIdentity> session,
        CancellationToken token) => ReadAsync(() => SourceBindings.ReadBinding(session, token), token);

    public Task<SessionWorkspaceEntry> ReadKnowledgeSourceTargetAsync(HostId<SessionIdentity> session,
        CancellationToken token) => ReadAsync(() => store.ReadMetadataAsync(session, token), token);

    internal Task<SessionKnowledgeSourceBinding> BindKnowledgeSourceAsync(SessionWorkspaceEntry target,
        KnowledgeSourceReference source, long version, Func<bool> admitted, CancellationToken token)
    {
        RequireNativeFileInput();
        return ControlAsync(target.Authority.SessionId, RequestOrigin.LocalUi,
            (request, eligible) => SourceBindings.Bind(request, target.Authority.Generation, source, version, eligible, token),
            token, admitted, existingSubject: true, resolveTerminalReceipt: true);
    }

    internal Task<bool> DetachKnowledgeSourceAsync(SessionKnowledgeSourceBinding exact, Func<bool> admitted,
        CancellationToken token)
    {
        RequireNativeFileInput();
        return ControlAsync(exact.Session, RequestOrigin.LocalUi, async (request, eligible) =>
        {
            await SourceBindings.Detach(request, exact, eligible, token).ConfigureAwait(false);
            return true;
        }, token, admitted, existingSubject: true, resolveTerminalReceipt: true);
    }
}
