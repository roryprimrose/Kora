using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    public async Task<SessionWorkOverviewRow> ReadSessionMetadataAsync(HostId<SessionIdentity> subject,
        CancellationToken token)
    {
        subject.Validate();
        using var timeout = new CancellationTokenSource(WorkOverviewTimeout, overviewTime ?? TimeProvider.System);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var revision = access.ControlRevision;
        var epoch = Interlocked.Read(ref workOverviewEpoch);
        try
        {
            return await ReadAsync(async () =>
            {
                RequireWorkOverviewAdmission(revision, epoch, cancellation.Token);
                var entry = await store.ReadMetadataAsync(subject, cancellation.Token).ConfigureAwait(false);
                ValidateListEntry(entry);
                if (entry.Authority.SessionId != subject)
                { throw new InvalidDataException("The passive exact subject lookup returned another session."); }
                var row = await ObserveOverviewAsync(entry, cancellation.Token).ConfigureAwait(false);
                RequireWorkOverviewAdmission(revision, epoch, cancellation.Token);
                return row;
            }, cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (cancellation.IsCancellationRequested)
        {
            if (timeout.IsCancellationRequested && !token.IsCancellationRequested)
            { throw new TimeoutException("The exact metadata read timed out; its physical read has joined.", exception); }
            throw new OperationCanceledException("The exact metadata read was cancelled and joined.", exception, token);
        }
    }
}
