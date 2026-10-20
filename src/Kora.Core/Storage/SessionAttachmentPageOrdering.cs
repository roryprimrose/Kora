namespace Kora.Core.Storage;

/// <summary>Pure ordering over a complete validated page; continuation and authority remain unchanged.</summary>
public static class SessionAttachmentPageOrdering
{
    public static IReadOnlyList<SessionAttachmentSummary> Apply(SessionAttachmentPage page,
        SessionAttachmentPageOrder order = SessionAttachmentPageOrder.Source)
    {
        ArgumentNullException.ThrowIfNull(page);
        page.Validate();
        // The admitted projection has no names. Both name directions therefore contain only
        // unavailable names and resolve by the authoritative unique retained-slot session key.
        var ordered = order switch
        {
            SessionAttachmentPageOrder.Source => null,
            SessionAttachmentPageOrder.DisplayNameAscending or SessionAttachmentPageOrder.DisplayNameDescending =>
                page.Records.OrderBy(row => row.Session.Value.ToString("D"), StringComparer.Ordinal),
            SessionAttachmentPageOrder.RetainedBytesAscending => page.Records
                .OrderBy(row => row.RetainedStoredBytes is null).ThenBy(row => row.RetainedStoredBytes)
                .ThenBy(row => row.Session.Value.ToString("D"), StringComparer.Ordinal),
            SessionAttachmentPageOrder.RetainedBytesDescending => page.Records
                .OrderBy(row => row.RetainedStoredBytes is null).ThenByDescending(row => row.RetainedStoredBytes)
                .ThenBy(row => row.Session.Value.ToString("D"), StringComparer.Ordinal),
            _ => throw new InvalidDataException("Unknown loaded-page order. Clear order to restore Source order; no read."),
        };
        return ordered is null ? page.Records : [.. ordered];
    }
}
