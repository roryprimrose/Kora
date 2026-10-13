using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Core.Storage;

/// <summary>Observed saved composer metadata, never a body-integrity receipt or permission.</summary>
public sealed record SavedDraftSummary(Guid Source, HostId<DeviceProfileIdentity> Profile,
    WorkSessionAuthorization Session, SessionMetadata? Metadata, long DraftRevision,
    long Utf8Bytes, long UnicodeScalars, long ControlRevision, long AuthorityCeiling, string AuthorityDigest)
{
    public void Validate()
    {
        Profile.Validate();
        Session.SessionId.Validate();
        _ = new HostRevision(Session.Generation.Value);
        if (Source == Guid.Empty || DraftRevision <= 0 || Utf8Bytes is <= 0 or > ConversationText.MaximumUtf8Bytes
            || UnicodeScalars is <= 0 or > ConversationText.MaximumUtf16Characters || ControlRevision < 0
            || AuthorityCeiling <= 0 || AuthorityDigest is null || AuthorityDigest.Length != 64
            || AuthorityDigest.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        { throw new InvalidDataException("Saved draft metadata is invalid or lacks exact private source authority."); }
        if (Metadata is { } metadata)
        {
            if (metadata.SessionId != Session.SessionId)
            { throw new InvalidDataException("Saved draft name metadata belongs to another session."); }
            _ = new HostRevision(metadata.Revision.Value);
            _ = new SessionName(metadata.Name.Value);
        }
    }
}
