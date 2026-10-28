using System.Runtime.InteropServices;
using System.Security.Principal;

using Kora.Core.Platform;
using Kora.Core.Skills;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Skills;

internal sealed partial class WindowsFixedSessionPlatform(ISessionController session) : IWindowsFixedSessionPlatform
{
    public bool IsCurrentInteractiveSession() => session.IsCurrentSessionUnlocked();

    public FixedSessionEffectResult Lock() => NativeResult(LockWorkStation());

    public IWindowsShutdownAuthority DuplicateShutdownAuthority() => new ShutdownAuthority();

    private static FixedSessionEffectResult NativeResult(bool accepted) => accepted
        ? new(FixedSessionEffectOutcome.RequestAccepted)
        : new(FixedSessionEffectOutcome.Failed, Marshal.GetLastPInvokeError());

    private sealed class ShutdownAuthority : IWindowsShutdownAuthority
    {
        private readonly SafeAccessTokenHandle token;
        private TokenPrivileges original;
        private bool activationAttempted;
        private bool enabled;
        private bool requested;
        private bool restored;
        private bool disposed;

        internal ShutdownAuthority()
        {
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query | TokenAccessLevels.Duplicate);
            if (!DuplicateTokenEx(identity.AccessToken, 0x0028 | 0x0004, 0, 2, 2, out token))
            { throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()); }
        }

        public int Enable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (activationAttempted) { throw new InvalidOperationException("Shutdown authority activation is single-use."); }
            activationAttempted = true;
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid))
            { return Marshal.GetLastPInvokeError(); }
            var requestedPrivileges = new TokenPrivileges { Count = 1, Privilege = luid, Attributes = 2 };
            if (!AdjustTokenPrivileges(token, false, in requestedPrivileges, (uint)Marshal.SizeOf<TokenPrivileges>(),
                out original, out _))
            { return Marshal.GetLastPInvokeError(); }
            var error = Marshal.GetLastPInvokeError();
            enabled = error == 0;
            return error;
        }

        public FixedSessionEffectResult Request(FixedSessionAction action)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled || restored || requested || action is not (FixedSessionAction.Shutdown or FixedSessionAction.Restart))
            { throw new InvalidOperationException("An active, unused private shutdown authority is required."); }
            requested = true;
            return WindowsIdentity.RunImpersonated(token, () =>
                NativeResult(ExitWindowsEx(action == FixedSessionAction.Shutdown ? 0x00000008u : 0x00000002u,
                    0x80040000)));
        }

        public int Restore()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!enabled || restored) { throw new InvalidOperationException("Only an active private authority can be restored."); }
            restored = true;
            if (!RestoreTokenPrivileges(token, false, in original, 0, 0, 0))
            { return Marshal.GetLastPInvokeError(); }
            return Marshal.GetLastPInvokeError();
        }

        public void Dispose()
        {
            if (disposed) { return; }
            try
            {
                if (enabled && !restored)
                {
                    var error = Restore();
                    if (error != 0) { throw new System.ComponentModel.Win32Exception(error); }
                }
            }
            finally { disposed = true; token.Dispose(); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        internal uint Low;
        internal int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        internal uint Count;
        internal Luid Privilege;
        internal uint Attributes;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockWorkStation();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ExitWindowsEx(uint flags, uint reason);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateTokenEx(SafeAccessTokenHandle existing, uint access, nint attributes,
        int impersonationLevel, int tokenType, out SafeAccessTokenHandle duplicate);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? system, string name, out Luid luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(SafeAccessTokenHandle token,
        [MarshalAs(UnmanagedType.Bool)] bool disableAll, in TokenPrivileges next,
        uint size, out TokenPrivileges previous, out uint required);

    [LibraryImport("advapi32.dll", EntryPoint = "AdjustTokenPrivileges", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RestoreTokenPrivileges(SafeAccessTokenHandle token,
        [MarshalAs(UnmanagedType.Bool)] bool disableAll, in TokenPrivileges next,
        uint size, nint previous, nint required);
}