using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

/// <summary>Consumer seam for the existing private native preparation pane; grants no queue authority.</summary>
public interface ISessionQueuePrerequisitePreparePresenter
{
    Task<bool> PrepareQueuePrerequisiteAsync(SessionQueuePrerequisitePrepareCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token);
}
