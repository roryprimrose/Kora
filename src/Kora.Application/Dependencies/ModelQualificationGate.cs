namespace Kora.Application.Dependencies;

[Flags]
internal enum ModelQualificationGate
{
    None = 0,
    LocalCandidateSelection = 1,
    IntegratedHost = 2,
    DotNetFinalRequest = 4,
    AllPathLifecycle = 8,
    ExecutionAccount = 16,
}
