using System.Diagnostics;
using System.Runtime.InteropServices;

using Kora.Core.Platform;
using Kora.Windows.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Session;

internal sealed class WindowsSessionNotificationWindow : IDisposable
{
    private const uint SessionChange = 0x02B1;
    private const uint Close = 0x0010;
    private const uint Destroy = 0x0002;
    private readonly Action<WindowsPrivacySignalEventArgs> changed;
    private readonly ILogger logger;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly WindowProcedure procedure;
    private readonly int sessionId;
    private nint window;
    private volatile bool registered;
    private volatile bool disposed;

    public WindowsSessionNotificationWindow(Action<WindowsPrivacySignalEventArgs> changed, ILogger logger)
    {
        this.changed = changed;
        this.logger = logger;
        using var process = Process.GetCurrentProcess();
        sessionId = process.SessionId;
        procedure = OnMessage;
        thread = new Thread(Run) { IsBackground = true, Name = "Kora Windows privacy observer" };
        thread.Start();
#pragma warning disable VSTHRD002 // A bounded native observer startup, independent of the UI dispatcher.
        _ = ready.Wait(TimeSpan.FromSeconds(2));
#pragma warning restore VSTHRD002
    }

    public bool IsRegistered => registered;

    public static WindowsSessionState ReadSessionState()
    {
        if (!WTSQuerySessionInformation(nint.Zero, -1, 25, out var buffer, out var bytes))
        {
            return WindowsSessionState.Unknown;
        }

        try
        {
            if (bytes < Marshal.SizeOf<SessionInfo>())
            {
                return WindowsSessionState.Unknown;
            }

            var info = Marshal.PtrToStructure<SessionInfo>(buffer);
            using var process = Process.GetCurrentProcess();
            if (info.Level != 1 || info.SessionId != process.SessionId)
            {
                return WindowsSessionState.Unknown;
            }

            if (info.ConnectionState != 0)
            {
                return WindowsSessionState.Disconnected;
            }

            return info.Flags switch
            {
                0 => WindowsSessionState.Locked,
                1 => WindowsSessionState.Unlocked,
                _ => WindowsSessionState.Unknown,
            };
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    public void Dispose()
    {
        disposed = true;
        var handle = window;
        if (handle != nint.Zero)
        {
            _ = PostMessage(handle, Close, nint.Zero, nint.Zero);
        }

        if (Thread.CurrentThread != thread)
        {
#pragma warning disable VSTHRD002 // Bounded join of an owned native message thread, never a task/UI wait.
            _ = thread.Join(TimeSpan.FromSeconds(2));
#pragma warning restore VSTHRD002
        }
    }

    private void Run()
    {
        var className = $"KoraPrivacy_{Guid.NewGuid():N}";
        var module = GetModuleHandle(null);
        try
        {
            var definition = new WindowClass
            {
                Procedure = procedure,
                Instance = module,
                ClassName = className,
            };
            if (RegisterClass(ref definition) == 0)
            {
                return;
            }

            window = CreateWindow(0, className, string.Empty, 0, 0, 0, 0, 0,
                nint.Zero, nint.Zero, module, nint.Zero);
            registered = window != nint.Zero && WTSRegisterSessionNotification(window, 0);
            ready.Set();
            if (!registered || disposed)
            {
                return;
            }

            while (GetMessage(out var message, nint.Zero, 0, 0) > 0)
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            WindowsLog.Error(logger, exception, "Monitoring Windows session privacy");
        }
        finally
        {
            registered = false;
            ready.Set();
            if (window != nint.Zero)
            {
                _ = WTSUnRegisterSessionNotification(window);
                _ = DestroyWindow(window);
                window = nint.Zero;
            }

            _ = UnregisterClass(className, module);
            if (!disposed)
            {
                changed(new WindowsPrivacySignalEventArgs(WindowsSessionState.Unknown,
                    reason: WindowsPrivacyChangeReason.Session));
            }
        }
    }

    private nint OnMessage(nint handle, uint message, nint wParam, nint lParam)
    {
        if (message == SessionChange && (int)lParam == sessionId)
        {
            var observation = PrivacyObservation.Create();
            var state = (int)wParam switch
            {
                2 or 4 => WindowsSessionState.Disconnected,
                6 => WindowsSessionState.SignedOut,
                7 => WindowsSessionState.Locked,
                1 or 3 or 5 or 8 => WindowsSessionState.Unlocked,
                _ => WindowsSessionState.Unknown,
            };
            changed(new WindowsPrivacySignalEventArgs(state, reason: WindowsPrivacyChangeReason.Session,
                observation: observation));
        }
        else if (message == Close)
        {
            _ = DestroyWindow(handle);
            return nint.Zero;
        }
        else if (message == Destroy)
        {
            PostQuitMessage(0);
            return nint.Zero;
        }

        return DefWindowProc(handle, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct SessionInfo
    {
        [FieldOffset(0)] public int Level;
        [FieldOffset(8)] public int SessionId;
        [FieldOffset(12)] public int ConnectionState;
        [FieldOffset(16)] public int Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public WindowProcedure? Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string? ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

#pragma warning disable SYSLIB1054 // Native window class contains a rooted managed WndProc delegate.
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(nint server, int session, int info, out nint buffer, out int bytes);
    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(nint buffer);
    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(nint window);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WindowClass definition);
    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string name, nint module);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindow(uint extended, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint module, nint parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out NativeMessage message, nint window, uint minimum, uint maximum);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(ref NativeMessage message);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);
#pragma warning restore SYSLIB1054
}
