namespace Kora.Core.Coordination;

public interface IInstanceLifecycleController
{
    // The existing user-approved restart flow requests this before desktop shutdown.
    // Actual launch is deferred until safe desktop/service disposal and exclusive release.
    void RequestRestartAfterQuiescence();
}
