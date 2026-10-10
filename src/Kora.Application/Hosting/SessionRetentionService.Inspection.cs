namespace Kora.Application.Hosting;

public sealed partial class SessionRetentionService
{
    public void RequirePassiveInspection()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!configuration.Available || !access.CanControl)
        {
            throw new InvalidOperationException("Session inspection is held by unavailable retention preferences or host ownership/privacy/call admission.");
        }
    }
}
