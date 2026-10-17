#if DEBUG
namespace Kora.Windows.Coordination;

internal sealed record DebugRecoveryOwner(int ProcessId, int SessionId, long CreationTime);
#endif
