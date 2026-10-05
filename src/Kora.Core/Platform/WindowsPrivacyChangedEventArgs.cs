namespace Kora.Core.Platform;

public sealed class WindowsPrivacyChangedEventArgs(
    WindowsPrivacySnapshot previous,
    WindowsPrivacySnapshot current,
    WindowsPrivacyChangeReason reason = WindowsPrivacyChangeReason.Unknown) : EventArgs
{
    public WindowsPrivacySnapshot Previous { get; } = previous;

    public WindowsPrivacySnapshot Current { get; } = current;

    public WindowsPrivacyChangeReason Reason { get; } = reason;
}
