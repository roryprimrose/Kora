namespace Kora.Core.Storage;

/// <summary>A passive metadata-only consumer seam over the initialized private interaction store.</summary>
public interface ISessionAttachmentCatalogueStore
{
    /// <summary>Reads a bounded content-free page under unchanged private admission.</summary>
    ValueTask<SessionAttachmentPage> ReadAttachmentPage(SessionAttachmentQuery query, string? cursor,
        Func<bool> admitted, CancellationToken token);

    /// <summary>Re-resolves the selected exact slot; replacement never retargets the selection.</summary>
    ValueTask VerifyAttachmentSummary(SessionAttachmentSummary exact, Func<bool> admitted, CancellationToken token);
}
