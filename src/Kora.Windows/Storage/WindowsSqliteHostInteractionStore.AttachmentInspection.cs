using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : ISessionAttachmentInspectionStore
{
    public async ValueTask<SessionAttachmentSummary?> ReadAttachmentInspection(HostId<SessionIdentity> session,
        Func<bool> admitted, CancellationToken token)
    {
        session.Validate();
        ArgumentNullException.ThrowIfNull(admitted);
        var current = HostActivity.RequireCurrent();
        if (current.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || current.Request.InvocationId is not null || current.Outcome != HostOperationOutcome.Unknown
            || current.Request.SessionId != session)
        { throw new InvalidOperationException("Exact retained inspection requires live original-user authority for this session."); }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(SessionAttachmentPage.RequestSeconds));
        try
        {
            var result = await Task.Run(() =>
            {
                RequireCatalogueAdmission(admitted, deadline.Token);
                using var lease = database.AcquireReadLease(deadline.Token);
                var identity = RequireInspectionIdentity();
                using var connection = database.OpenReadOnly(deadline.Token);
                using var transaction = connection.BeginTransaction(deferred: true);
                _ = RequireCatalogueProfile(connection);
                _ = ValidateCatalogueAuthority(connection, identity, deadline.Token);
                using var command = AttachmentMetadataCommand(connection, null, session.Value);
                checkpoint?.BeforeAttachmentMetadataRead(command);
                using var row = command.ExecuteReader();
                var exact = row.Read() ? DecodeCatalogueRow(connection, row, admitted, deadline.Token) : null;
                VerifyCatalogueSource(identity, admitted, deadline.Token);
                return exact;
            }, deadline.Token).ConfigureAwait(false);
            RequireCatalogueAdmission(admitted, deadline.Token);
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
