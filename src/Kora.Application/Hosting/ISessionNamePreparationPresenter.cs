using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public interface ISessionNamePreparationPresenter
{
    Task<bool> PrepareNameAsync(SessionNamePreparationCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token);
}
