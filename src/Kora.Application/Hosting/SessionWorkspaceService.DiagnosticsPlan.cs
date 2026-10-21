using System.Diagnostics;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    public async Task<SessionDiagnosticsPlanPreparation> PrepareDiagnosticsPlanAsync(HostId<SessionIdentity> session,
        IReadOnlyList<SessionQueueReadProfile> profiles, Func<bool> admitted, CancellationToken token)
    {
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        try
        {
            var revision = access.ControlRevision;
            bool Eligible() => access.CanInspect && access.CanControl && access.ControlRevision == revision
                && admitted() && !token.IsCancellationRequested;
            if (!Eligible()) { throw new OperationCanceledException("Diagnostics preparation admission changed."); }
            var work = await ReadWorkAsync(session, token).ConfigureAwait(false);
            var review = await ReviewDiagnosticsPlanAsync(work.Queue, profiles, SessionDiagnosticsPlanPrerequisite.None,
                Eligible, token).ConfigureAwait(false);
            var preparation = new SessionDiagnosticsPlanPreparation(work with { Queue = review.Queue }, review, activity.Activity!.Context);
            try
            {
                await ValidateDiagnosticsPlanPreparationAsync(preparation, Eligible, token).ConfigureAwait(false);
                activity.Complete(HostOperationOutcome.Completed);
                return preparation;
            }
            catch
            {
                RetireDiagnosticsPlan(review);
                throw;
            }
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    public async Task ValidateDiagnosticsPlanPreparationAsync(SessionDiagnosticsPlanPreparation preparation,
        Func<bool> admitted, CancellationToken token)
    {
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), preparation.Review.Queue.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request,
            [new ActivityLink(preparation.Cause)]);
        try
        {
            var source = await ReadAsync(() => store.ReadMetadataAsync(preparation.Review.Queue.SessionId, token), token)
                .ConfigureAwait(false);
            if (source != preparation.Work.Session)
            { throw new InvalidOperationException("The original exact diagnostics session source changed. Request a fresh plan."); }
            await DiagnosticsQueue.ValidateDiagnosticsPlanAsync(preparation.Review, admitted, token).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    public Task<SessionDiagnosticsPlanReview> ReviewDiagnosticsPlanAsync(SessionQueueSnapshot expected,
        IReadOnlyList<SessionQueueReadProfile> profiles, SessionDiagnosticsPlanPrerequisite prerequisite,
        Func<bool> admitted, CancellationToken token) =>
        DiagnosticsQueue.ReviewDiagnosticsPlanAsync(expected, profiles, prerequisite, RequestOrigin.LocalUi, admitted, token);

    public Task<SessionDiagnosticsPlanOutcome> ConfirmDiagnosticsPlanAsync(SessionDiagnosticsPlanReview review,
        Func<bool> admitted, CancellationToken token) =>
        DiagnosticsQueue.ConfirmDiagnosticsPlanAsync(review, RequestOrigin.LocalUi, admitted, token);

    public void RetireDiagnosticsPlan(SessionDiagnosticsPlanReview review) => queue?.RetireDiagnosticsPlan(review);

    private SessionQueueService DiagnosticsQueue => queue
        ?? throw new InvalidOperationException("The fixed local diagnostics queue is unavailable.");
}
