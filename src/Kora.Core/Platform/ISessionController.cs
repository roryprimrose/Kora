namespace Kora.Core.Platform;

public interface ISessionController
{
    bool IsCurrentSessionUnlocked();

    bool LockCurrentSession();
}