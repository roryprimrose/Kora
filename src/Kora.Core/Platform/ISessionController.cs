namespace Kora.Core.Platform;

public interface ISessionController
{
    bool LockCurrentSession();
}