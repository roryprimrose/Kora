using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Host-only startup preference control, including manual presentation; confers no audio, network or launch authority.</summary>
public interface ILogonStartupSessionStore
{
    ValueTask<WorkSessionAuthorization> CreateLogonStartupSession(HostRequest request, Func<bool> admitted, CancellationToken token);
    ValueTask<T> WithLogonStartupSession<T>(HostRequest request, HostRevision generation, Func<T> operation, CancellationToken token);
}
