namespace Kora.Windows.Presentation;

internal interface IWindowsMotionPreferenceNative
{
    bool IsAnimationEnabled();
    IDisposable Subscribe(Action changed);
}
