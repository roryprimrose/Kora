namespace Kora.Controls;

internal interface IPresenceFrameScheduler
{
    TimeSpan Now { get; }

    IDisposable Schedule(TimeSpan delay, Action callback);
}
