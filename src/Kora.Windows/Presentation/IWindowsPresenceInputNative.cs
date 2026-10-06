namespace Kora.Windows.Presentation;

internal interface IWindowsPresenceInputNative
{
    bool IsControlPressed();

    bool IsMouseButtonPressed();

    uint GetExtendedStyle(nint window);

    void SetExtendedStyle(nint window, uint style);

    void EnableLayeredRendering(nint window);

    void ShowMoveCursorIfOverWindow(nint window);
}
