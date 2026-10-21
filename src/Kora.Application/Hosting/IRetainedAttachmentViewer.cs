using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Native-only disclosure seam. The host-held exact summary is not capture or mutation consent.</summary>
public interface IRetainedAttachmentViewer
{
    /// <summary>Opens only <paramref name="exact"/> for <paramref name="target"/> while <paramref name="admitted"/> and <paramref name="token"/> remain live.</summary>
    Task Open(SessionWorkspaceEntry target, SessionAttachmentSummary exact,
        Func<bool> admitted, CancellationToken token);
}
