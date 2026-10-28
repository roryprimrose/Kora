using System.Collections.Immutable;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Memory.DeviceProfile;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : IDeviceProfileMemoryStore
{
    public ValueTask<MemoryResult> TransactDeviceProfileMemoryAsync(HostRequest request, MemoryBoundary boundary,
        Func<ImmutableArray<MemoryRecord>, MemoryStorageCommit> transition, Func<bool> admitted, CancellationToken token) =>
        TransactMemoryCoreAsync(request, boundary, transition, admitted, MemoryScopeKind.DeviceProfile, token);

    private static Guid MemoryStorageSession(MemoryRecord record) => record.Lineage.Request.SessionId.Value;

    private static bool ValidMemoryScope(MemoryRecord record, HostId<DeviceProfileIdentity> profile) =>
        record.Scope.Kind switch
        {
            MemoryScopeKind.Session => record.Scope.Identity == record.Lineage.Request.SessionId.Value,
            MemoryScopeKind.DeviceProfile => record.Scope.Identity == profile.Value
                && record.Lineage.Origin == MemoryProposalOrigin.User,
            _ => false,
        };

    private static MemoryScope DecodeMemoryScope(MemoryScopeKind? kind, HostId<SessionIdentity> session, MemoryLineage lineage) =>
        kind switch
        {
            null => MemoryScope.Session(session),
            MemoryScopeKind.DeviceProfile when lineage.Request.SessionId == session => MemoryScope.DeviceProfile(lineage.Profile),
            _ => throw new InvalidDataException("The saved memory scope or original lineage is invalid."),
        };
}
