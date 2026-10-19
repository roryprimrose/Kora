using Avalonia;
using Avalonia.Controls;

namespace Kora;

internal sealed record PresenceWindowState(
    PixelPoint Position, WindowState WindowState, bool WasVisible, bool PresenceVisible,
    TimeSpan? TimeoutRemaining, long CapturedAt);
