namespace Kora.Core.Storage;

/// <summary>A volatile query/filter and host/store-lifetime bound mutable-keyset continuation, not a snapshot.</summary>
/// <param name="Host">The owning service's process-local store binding.</param>
/// <param name="AdmissionRevision">The private inspection admission revision.</param>
/// <param name="After">The last consumed canonical session ID, including nonmatches.</param>
/// <param name="QueryDigest">The exact validated query kind, filter and text digest; not diagnostic content.</param>
public sealed record SessionListSearchCursor(Guid Host, long AdmissionRevision, Guid After, string QueryDigest)
{
    public void Validate(Guid host, long revision, SessionListSearch search)
    {
        ArgumentNullException.ThrowIfNull(search);
        if (Host == Guid.Empty || Host != host || AdmissionRevision != revision || After == Guid.Empty
            || !string.Equals(QueryDigest, search.Digest, StringComparison.Ordinal)
            || search.Kind is SessionListSearchKind.ExactId)
        {
            throw new ArgumentException("The list continuation is invalid, expired or belongs to another query/filter/host. Start a fresh search.", nameof(search));
        }
    }
}
