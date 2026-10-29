using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : IBundledControlSessionStore
{
    private const string BundledControlAdmissionAction = "session.bundled.fixed.admit";
    private const string BundledControlSource = "bundled-fixed-action";

    public ValueTask<HostTaskRecord> AdmitBundledControlDispatchAsync(HostRequest request, HostRevision generation,
        Func<bool> admitted, CancellationToken cancellationToken)
    {
        if (request.InvocationId is not null)
        { throw new InvalidOperationException("Bundled control admission requires original input, not a tool invocation."); }
        return AdmitOriginalControlDispatchAsync(request, generation, admitted,
            BundledControlAdmissionAction, BundledControlSource, cancellationToken);
    }

    public ValueTask<WorkSessionAuthorization> CreateBundledControlSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken) =>
        RunHostMutationAsync(request, "session.create.bundled-control", (connection, transaction, intent, audit) =>
        {
            if (ReadSession(connection, request.SessionId) is not null || !admitted())
            { throw new InvalidOperationException("Bundled control admission changed or its identity already exists."); }
            var session = new WorkSessionAuthorization(request.SessionId, new(1), true);
            var metadata = new SessionMetadata(request.SessionId, new(1), new("Computer control"));
            var sequence = AppendAudit(connection, transaction, intent, session, audit,
                changes: [SessionChange(session, 0), MetadataChange(metadata)]);
            WriteSession(connection, transaction, session, state: 0, sequence);
            WriteMetadata(connection, transaction, metadata, sequence);
            return session;
        }, cancellationToken, admitted, requireIdle: false);
}