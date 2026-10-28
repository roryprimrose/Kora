using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public sealed record WorkspaceSurfaceNavigation(WorkspaceSurfaceTarget Target, Func<bool> IsAdmitted,
    int? SessionPosition = null, SessionName? ConversationName = null, RequestOrigin Origin = RequestOrigin.LocalUi)
{
    public HostRequest? Request { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public WorkspaceSurfaceNavigationOutcome Outcome { get; set; } = WorkspaceSurfaceNavigationOutcome.Unavailable;
}
