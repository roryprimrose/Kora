using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public interface ISessionQueueCancellationReviewPresenter
{
    Task<bool> PrepareQueueCancellationAsync(SessionQueueCancellationReviewCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token);
}
