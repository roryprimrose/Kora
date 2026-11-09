namespace Kora.Windows.Presentation;

public interface IWindowsPresenceWindowInput
{
    bool InterceptsMouse { get; }
    bool IsGestureActive { get; }
    bool Refresh(bool canInteract);
}
