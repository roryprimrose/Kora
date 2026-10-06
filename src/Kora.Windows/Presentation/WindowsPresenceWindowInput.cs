namespace Kora.Windows.Presentation;

public sealed class WindowsPresenceWindowInput
{
    private const uint TransparentStyle = 0x00000020;
    private const uint LayeredStyle = 0x00080000;
    private const uint NoActivateStyle = 0x08000000;
    private readonly nint window;
    private readonly IWindowsPresenceInputNative native;

    public WindowsPresenceWindowInput(nint window)
        : this(window, new WindowsPresenceInputNative())
    {
    }

    internal WindowsPresenceWindowInput(nint window, IWindowsPresenceInputNative native)
    {
        if (window == nint.Zero)
        {
            throw new ArgumentException("The presence window must have a native handle.", nameof(window));
        }

        this.window = window;
        this.native = native;
        ApplyInputMode(intercept: false);
    }

    public bool InterceptsMouse { get; private set; }

    public static uint GetExtendedStyle(uint currentStyle, bool intercept) =>
        intercept
            ? (currentStyle | LayeredStyle | NoActivateStyle) & ~TransparentStyle
            : currentStyle | LayeredStyle | NoActivateStyle | TransparentStyle;

    public bool Refresh(bool canInteract)
    {
        var intercept = canInteract && native.IsControlPressed();
        // Keep each mouse gesture with its original recipient, including a native move loop.
        if (canInteract && native.IsMouseButtonPressed())
        {
            intercept = InterceptsMouse;
        }

        if (intercept != InterceptsMouse)
        {
            ApplyInputMode(intercept);
        }

        if (InterceptsMouse)
        {
            native.ShowMoveCursorIfOverWindow(window);
        }

        return InterceptsMouse;
    }

    private void ApplyInputMode(bool intercept)
    {
        var currentStyle = native.GetExtendedStyle(window);
        var extendedStyle = GetExtendedStyle(currentStyle, intercept);
        if (currentStyle != extendedStyle)
        {
            native.SetExtendedStyle(window, extendedStyle);
        }
        native.EnableLayeredRendering(window);
        InterceptsMouse = intercept;
    }
}
