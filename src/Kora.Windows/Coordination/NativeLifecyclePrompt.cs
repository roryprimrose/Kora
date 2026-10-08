using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Kora.Windows.Coordination;

internal sealed partial class NativeLifecyclePrompt(string sid, int sessionId)
{
    private const uint CloseMessage = 0x0010;

    internal bool IsEligible()
    {
        try
        {
            return IsEligibleCore();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private bool IsEligibleCore()
    {
        if (!Environment.UserInteractive ||
            !WTSQuerySessionInformation(IntPtr.Zero, sessionId, 8, out var state, out var size))
        {
            return false;
        }

        try
        {
            if (size < sizeof(int) || Marshal.ReadInt32(state) != 0)
            {
                return false;
            }
        }
        finally
        {
            WTSFreeMemory(state);
        }

        // Secure/lock desktops cannot reveal either exact executable identity or assistant UI.
        var desktop = OpenInputDesktop(0, false, 1);
        if (desktop == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var buffer = Marshal.AllocHGlobal(1024);
            try
            {
                if (!GetUserObjectInformation(desktop, 2, buffer, 1024, out _) ||
                    !string.Equals(Marshal.PtrToStringUni(buffer), "Default", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = CloseDesktop(desktop);
        }

        return !HasAmbiguousSessions();
    }

    private bool HasAmbiguousSessions()
    {
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var sessions, out var count))
        {
            return true;
        }

        try
        {
            for (var i = 0; i < count; i++)
            {
                var session = Marshal.PtrToStructure<SessionInfo>(
                    sessions + (i * Marshal.SizeOf<SessionInfo>()));
                if (session.Id == sessionId || session.State is not (0 or 1))
                {
                    continue;
                }

                var user = QuerySessionText(session.Id, 5);
                if (string.IsNullOrEmpty(user))
                {
                    continue;
                }

                var domain = QuerySessionText(session.Id, 7);
                try
                {
                    var account = new NTAccount(domain, user);
                    var otherSid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
                    if (otherSid.Value.Equals(sid, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (IdentityNotMappedException)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            WTSFreeMemory(sessions);
        }
    }

    private static string QuerySessionText(int session, int information)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, session, information, out var buffer, out _))
        {
            throw new InvalidOperationException("The interactive session identity cannot be verified.");
        }

        try
        {
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    internal bool Ask(string question, TimeSpan timeout, Func<bool>? stillValid = null)
        => Display(question, timeout, stillValid, isQuestion: true);

    private bool Display(string text, TimeSpan timeout, Func<bool>? stillValid, bool isQuestion)
    {
        if (!Valid(stillValid))
        {
            return false;
        }

        var finished = new ManualResetEventSlim();
        var accepted = false;
        var invalidated = false;
        var promptThread = new Thread(() =>
        {
            try
            {
                Volatile.Write(ref promptThreadId, GetCurrentThreadId());
                if (!Valid(stillValid))
                {
                    return;
                }

                var result = MessageBox(IntPtr.Zero, text, "Kora — instance ownership",
                    GetDisplayFlags(isQuestion));
                accepted = isQuestion && result == 6;
            }
            finally
            {
                finished.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Kora lifecycle question",
        };
        promptThread.SetApartmentState(ApartmentState.STA);
        promptThread.Start();
        var elapsed = Stopwatch.StartNew();
        while (!finished.Wait(100))
        {
            if (invalidated || elapsed.Elapsed >= timeout || !Valid(stillValid))
            {
                invalidated = true;
                _ = EnumThreadWindows(Volatile.Read(ref promptThreadId), CloseWindow, IntPtr.Zero);
            }
        }

        finished.Dispose();
        return accepted && !invalidated && Valid(stillValid);
    }

    private bool Valid(Func<bool>? stillValid)
    {
        try
        {
            return IsEligible() && stillValid?.Invoke() != false;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    // MessageBox's owning native thread is published before any sensitive display.
    private uint promptThreadId;

    internal void ShowError(string message)
    {
        _ = Display($"{message}\n\nThis notice does not authorise another assistant or an automatic restart.",
            TimeSpan.FromSeconds(30), stillValid: null, isQuestion: false);
    }

    internal static uint GetDisplayFlags(bool isQuestion) => isQuestion
        ? 0x00000004u | 0x00000020u | 0x00000100u | 0x00040000u
        : 0x00000010u | 0x00040000u;

    private static bool CloseWindow(IntPtr window, IntPtr parameter)
    {
        // Yes/No MessageBox dialogs may ignore WM_CLOSE; explicitly select No as well.
        _ = PostMessage(window, 0x0111, new IntPtr(7), IntPtr.Zero);
        _ = PostMessage(window, CloseMessage, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SessionInfo
    {
        internal int Id;
        internal IntPtr StationName;
        internal int State;
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr owner, string text, string title, uint flags);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumThreadWindows(uint thread, EnumWindowsCallback callback, IntPtr parameter);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);

    [LibraryImport("user32.dll")]
    private static partial IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseDesktop(IntPtr desktop);

    [LibraryImport("user32.dll", EntryPoint = "GetUserObjectInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetUserObjectInformation(IntPtr handle, int index, IntPtr information, uint length, out uint needed);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSQuerySessionInformation(IntPtr server, int session, int information, out IntPtr buffer, out int bytes);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(IntPtr buffer);
}
