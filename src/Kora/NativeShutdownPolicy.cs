using Avalonia.Controls;

namespace Kora;

internal static class NativeShutdownPolicy
{
    internal static bool RequiresImmediateRetirement(WindowCloseReason reason) =>
        reason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
}
