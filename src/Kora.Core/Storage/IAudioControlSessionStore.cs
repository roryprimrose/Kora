using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Host-only audio preference admission, not a grant, question answer or execution token.</summary>
public interface IAudioControlSessionStore
{
    ValueTask<WorkSessionAuthorization> CreateAudioControlSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken);

    /// <summary>Serializes current durable intent/session generation with the admitted preference operation.</summary>
    ValueTask<T> WithAudioControlSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken);
}
