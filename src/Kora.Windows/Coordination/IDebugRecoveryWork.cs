#if DEBUG
namespace Kora.Windows.Coordination;

public interface IDebugRecoveryWork : IDisposable
{
    void Complete();
}
#endif
