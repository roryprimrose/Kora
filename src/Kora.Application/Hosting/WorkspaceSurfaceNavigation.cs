using Kora.Core.Commands;

namespace Kora.Application.Hosting;

public sealed record WorkspaceSurfaceNavigation(WorkspaceSurfaceTarget Target, Func<bool> IsAdmitted,
    int? SessionPosition = null);
