using System.Runtime.InteropServices;

namespace Kora.Windows.Coordination;

internal sealed partial class NativeLifecyclePrompt
{
    internal bool HasOtherUserSessions()
    {
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var sessions, out var count))
        {
            throw new InvalidOperationException("Setup cannot verify the other Windows user sessions.");
        }
        try
        {
            for (var index = 0; index < count; index++)
            {
                var session = Marshal.PtrToStructure<SessionInfo>(
                    sessions + (index * Marshal.SizeOf<SessionInfo>()));
                if (session.Id != sessionId && !string.IsNullOrEmpty(QuerySessionText(session.Id, 5)))
                {
                    return true;
                }
            }
            return false;
        }
        finally { WTSFreeMemory(sessions); }
    }
}
