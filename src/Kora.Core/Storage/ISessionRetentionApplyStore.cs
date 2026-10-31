using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Original-native, exact-session policy application through existing lifecycle transactions.</summary>
public interface ISessionRetentionApplyStore
{
    /// <summary>Reads exact metadata and copy availability without activity or maintenance.</summary>
    ValueTask<SessionRetentionApplyReview> ReviewRetentionApplyAsync(
        HostId<SessionIdentity> session, CancellationToken token);

    /// <summary>Fences the reviewed file/source before recording the operation's own control intent.</summary>
    ValueTask ValidateRetentionApplyAsync(SessionRetentionApplyReview review, CancellationToken token);

    /// <summary>Commits only the reviewed policy and due effect; uncertain copy completion throws.</summary>
    ValueTask<SessionRetentionApplyReceipt> ApplyReviewedRetentionAsync(HostRequest request,
        SessionRetentionApplyReview review, Func<bool> admitted, CancellationToken token);
}
