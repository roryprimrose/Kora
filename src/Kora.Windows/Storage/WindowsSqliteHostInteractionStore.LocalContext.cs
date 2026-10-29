using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : ILocalContextTaskStore
{
    private const string LocalContextAdmissionAction = "session.context.local.admit";
    private const string LocalContextSource = "local-context";

    public ValueTask<HostTaskRecord> AdmitLocalContextDispatchAsync(HostRequest request, HostRevision generation,
        Func<bool> eligible, CancellationToken token) =>
        AdmitOriginalControlDispatchAsync(request, generation, eligible, LocalContextAdmissionAction, LocalContextSource, token);

    private HostTaskObservation? ObserveLocalContextTask(SqliteConnection connection,
        HostTaskRecord task, WorkSessionAuthorization session) =>
        ObserveOriginalControlTask(connection, task, session, LocalContextAdmissionAction, LocalContextSource);
}