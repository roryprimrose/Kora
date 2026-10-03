using System.Runtime.InteropServices;

using Kora.Core.Platform;

namespace Kora.Windows.Session;

public sealed partial class WindowsSessionController : ISessionController
{
    public bool LockCurrentSession() => LockWorkStation();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockWorkStation();
}