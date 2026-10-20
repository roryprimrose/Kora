using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

/// <summary>Prepares the existing exact expired replacement review in native Sessions only.</summary>
public interface ISessionQueueReplacementReviewPresenter
{
    Func<bool> CaptureLifetime();
    Task<bool> ReviewReplacementAsync(SessionQueueReplacementReviewCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token);
}
