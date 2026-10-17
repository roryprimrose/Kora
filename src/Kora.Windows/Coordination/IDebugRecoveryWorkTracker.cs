#if DEBUG
namespace Kora.Windows.Coordination;

/// <summary>Records external work before it can outlive the Debug assistant owner.</summary>
public interface IDebugRecoveryWorkTracker
{
    IDebugRecoveryWork BeginWork();
}

#endif
