using System.Diagnostics;

using Avalonia.Threading;

namespace Kora.Controls;

internal sealed class DispatcherPresenceFrameScheduler : IPresenceFrameScheduler
{
    public TimeSpan Now => Stopwatch.GetElapsedTime(0, Stopwatch.GetTimestamp());

    public IDisposable Schedule(TimeSpan delay, Action callback) =>
        DispatcherTimer.RunOnce(callback, delay, DispatcherPriority.Background);
}
