using System.ComponentModel;
using System.Runtime.InteropServices;

using Kora.Core.Presentation;

namespace Kora.Windows.Presentation;

public sealed partial class WindowsPresenceRenderTarget : IPresenceRenderTarget
{
    private static readonly WindowProcedure Procedure = OnMessage;
    private readonly string className = $"KoraPresence_{Guid.NewGuid():N}";
    private readonly nint instance;
    private readonly int uiThread = Environment.CurrentManagedThreadId;
    private WindowsPresenceDib? buffer;
    private int drawingThread;
    private bool disposed;

    public WindowsPresenceRenderTarget(nint parent)
    {
        if (parent == 0)
        {
            throw new ArgumentException("The native presence requires its host-owned parent.", nameof(parent));
        }
        var parentThread = GetWindowThreadProcessId(parent, out var parentProcess);
        if (parentProcess != Environment.ProcessId || parentThread != GetCurrentThreadId())
        {
            throw new InvalidOperationException("The native presence parent must belong to the current process and UI thread.");
        }
        instance = GetModuleHandle(null);
        // NativeControlHost supplies a private intermediate holder. It too must pass input to the shell.
        var nativeInput = new WindowsPresenceInputNative();
        nativeInput.SetExtendedStyle(parent, nativeInput.GetExtendedStyle(parent) | 0x08000020u);
        var definition = new WindowClass
        {
            Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance = instance,
            ClassName = className,
        };
        Check(RegisterClass(ref definition) != 0, "Registering the native presence");
        try
        {
            // A real child inherits the parent's native visibility, clipping, placement and DPI lifetime.
            Handle = CreateWindowEx(0x08080020, className, string.Empty, 0x40000000,
                0, 0, 1, 1, parent, 0, instance, 0);
            Check(Handle != 0, "Creating the native presence child");
        }
        catch
        {
            Check(UnregisterClass(className, instance), "Unregistering the failed native presence");
            throw;
        }
    }

    public nint Handle { get; }
    public long FramesPresented { get; private set; }

    public void Draw(ReadOnlySpan<PresenceParticle> particles, double width, double height, double scaling,
        byte red, byte green, byte blue, double opacity)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var thread = Environment.CurrentManagedThreadId;
        if (drawingThread == 0)
        {
            drawingThread = thread;
        }
        if (drawingThread != thread || thread == uiThread)
        {
            throw new InvalidOperationException("Presence drawing resources belong to one non-UI rendering worker.");
        }
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(height) || height <= 0
            || !double.IsFinite(scaling) || scaling <= 0 || !double.IsFinite(opacity) || opacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scaling));
        }
        var pixelWidth = checked((int)Math.Ceiling(width * scaling));
        var pixelHeight = checked((int)Math.Ceiling(height * scaling));
        foreach (var particle in particles)
        {
            if (!double.IsFinite(particle.X) || !double.IsFinite(particle.Y)
                || !double.IsFinite(particle.Radius) || particle.Radius <= 0 || particle.Depth is < 0 or > 5)
            {
                throw new InvalidDataException("A native presence dot has invalid geometry or opacity band.");
            }
        }
        if (buffer is null || buffer.Width != pixelWidth || buffer.Height != pixelHeight)
        {
            buffer?.Dispose();
            buffer = null;
            buffer = new WindowsPresenceDib(pixelWidth, pixelHeight);
        }
        buffer.Draw(particles, scaling, red, green, blue, opacity);
    }

    public void Present()
    {
        VerifyUiThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        var current = buffer ?? throw new InvalidOperationException("The native presence has no completed frame.");
        var size = new NativeSize { Width = current.Width, Height = current.Height };
        var source = new NativePoint();
        var blend = new BlendFunction { Alpha = 255, Format = 1 };
        // Null destination preserves the child position managed by the Avalonia host.
        Check(UpdateLayeredWindow(Handle, 0, 0, ref size, current.DeviceContext, ref source, 0, ref blend, 2),
            "Presenting the native presence");
        _ = ShowWindow(Handle, 4);
        FramesPresented++;
    }

    public void Hide()
    {
        VerifyUiThread();
        if (!disposed)
        {
            _ = ShowWindow(Handle, 0);
        }
    }

    public void ReleaseDrawingResources()
    {
        if (drawingThread != 0 && drawingThread != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Presence drawing resources must be released on their owning worker.");
        }
        buffer?.Dispose();
        buffer = null;
    }

    public void Dispose()
    {
        VerifyUiThread();
        if (disposed)
        {
            return;
        }
        if (buffer is not null)
        {
            throw new InvalidOperationException("The presence worker must release its buffer before destroying the child window.");
        }
        disposed = true;
        try
        {
            if (IsWindow(Handle))
            {
                Check(DestroyWindow(Handle), "Destroying the native presence");
            }
        }
        finally
        {
            Check(UnregisterClass(className, instance), "Unregistering the native presence");
        }
    }

    private void VerifyUiThread()
    {
        if (Environment.CurrentManagedThreadId != uiThread)
        {
            throw new InvalidOperationException("Native presence presentation and window lifetime belong to the UI thread.");
        }
    }

    private static nint OnMessage(nint window, uint message, nint wparam, nint lparam) =>
        message == 0x0084 ? -1 : DefWindowProc(window, message, wparam, lparam);

    private static void Check(bool succeeded, string operation)
    {
        if (!succeeded)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), operation);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint hwnd, uint message, nint wparam, nint lparam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction { public byte Operation; public byte Flags; public byte Alpha; public byte Format; }

    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? name);
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClass(string className, nint instance);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint hwnd, uint message, nint wparam, nint lparam);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(nint window);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(nint window, nint destinationDc, nint destination,
        ref NativeSize size, nint sourceDc, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);
}