using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

/// <summary>Prepares only the existing private native adjacent-move review.</summary>
public interface ISessionQueueMoveReviewPresenter
{
    Func<bool> CaptureNavigationEligibility();

    Task<bool> ReviewQueueMoveAsync(SessionQueueMoveReviewCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token);
}
