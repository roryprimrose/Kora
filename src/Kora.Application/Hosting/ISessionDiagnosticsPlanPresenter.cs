using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

/// <summary>Captures the exact existing review before deferred opening of its guarded native owner.</summary>
public interface ISessionDiagnosticsPlanPresenter
{
    Task<bool> PrepareDiagnosticsPlanAsync(SessionDiagnosticsPlanCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token);
}
