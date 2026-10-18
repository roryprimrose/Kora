using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
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
