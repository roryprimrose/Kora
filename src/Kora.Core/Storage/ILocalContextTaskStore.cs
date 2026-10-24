using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Commits content-free, current-run local context source ownership and dispatch as one authority transaction.</summary>
public interface ILocalContextTaskStore
{
    /// <summary>Requires the exact original intent and reviewed active generation; no prompt or source text is accepted.</summary>
    ValueTask<HostTaskRecord> AdmitLocalContextDispatchAsync(HostRequest request, HostRevision generation,
        Func<bool> eligible, CancellationToken token);
}