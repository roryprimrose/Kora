using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

/// <summary>Opens only the existing private native logical-disposition review.</summary>
public interface ISessionDispositionReviewPresenter
{
    /// <summary>Returns whether the exact preview was displayed, without confirming or removing anything.</summary>
    Task<bool> ReviewDispositionAsync(SessionDispositionReviewCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token);
}
