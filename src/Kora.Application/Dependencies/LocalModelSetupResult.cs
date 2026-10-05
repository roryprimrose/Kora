using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed record LocalModelSetupResult(
    IReadOnlyList<DependencyStatus> Statuses,
    DependencyStatus Inference);