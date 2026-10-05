namespace Kora.Core.Coordination;

public interface IInstanceCoordinator : IDisposable, IInstanceLifecycleController
{
    string? FailureReason { get; }

    InstanceBuildIdentity BuildIdentity { get; }

    InstanceStartupDisposition Enter();

    void CompleteHostExit(bool safelyDisposed);
}
