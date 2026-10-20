using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    public Task<(SessionWorkSnapshot Work, SessionQueueMoveReview Review, Func<bool> IsCurrent)> PrepareNativeMoveReviewAsync(
        SessionQueueMoveReviewCommand command, RequestOrigin origin, Func<bool> admitted, CancellationToken token) =>
        (queue ?? throw new InvalidOperationException("The authoritative queue service is unavailable."))
            .PrepareNativeMoveReviewAsync(command, origin, admitted, token);
}
