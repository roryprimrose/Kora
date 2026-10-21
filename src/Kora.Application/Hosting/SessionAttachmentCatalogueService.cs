using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Passive catalogue and exact navigation preparation; never persistence, resume or source consent.</summary>
public sealed class SessionAttachmentCatalogueService(SessionWorkspaceService sessions, SessionFileAttachmentService files)
{
    public async Task<SessionAttachmentPage> Read(SessionAttachmentQuery query, string? cursor,
        Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(admitted);
        SessionWorkspaceService.RequireNativeFileInput();
        var result = await sessions.ReadAttachmentCatalogue(query, cursor, admitted, token).ConfigureAwait(false);
        result.Validate();
        if (result.Query != query) { throw new InvalidDataException("The attachment page does not match the original native metadata query."); }
        token.ThrowIfCancellationRequested();
        if (!admitted()) { throw new InvalidOperationException("The private attachment catalogue was revoked before disclosure."); }
        return result;
    }

    public Task<SessionWorkspaceEntry> ResolveExact(SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token)
    {
        SessionWorkspaceService.RequireNativeFileInput();
        return files.ResolveInspection(exact, admitted, token);
    }
}
