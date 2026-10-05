using System.Runtime.InteropServices;

using Kora.Core.Platform;
using Kora.Windows.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Identity;

public sealed partial class WindowsCurrentUserNameProvider(
    ILogger<WindowsCurrentUserNameProvider> logger) : ICurrentUserNameProvider
{
    private const int NameDisplay = 3;

    public string? GetAddressName()
    {
        var displayName = GetDisplayName();
        return ExtractAddressName(displayName)
            ?? ExtractAddressName(Environment.UserName);
    }

    internal static string? ExtractAddressName(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return null;
        }

        var domainSeparator = candidate.LastIndexOf('\\');
        if (domainSeparator >= 0 && domainSeparator < candidate.Length - 1)
        {
            candidate = candidate[(domainSeparator + 1)..];
        }

        var emailSeparator = candidate.IndexOf('@');
        if (emailSeparator > 0)
        {
            candidate = candidate[..emailSeparator];
        }

        var separator = candidate.IndexOfAny([' ', '.', '_']);
        if (separator > 0)
        {
            candidate = candidate[..separator];
        }

        candidate = candidate.Trim();
        return candidate.Any(char.IsLetterOrDigit) ? candidate : null;
    }

    private unsafe string? GetDisplayName()
    {
        uint length = 0;
        _ = GetUserNameEx(NameDisplay, null, ref length);
        if (length == 0)
        {
            WindowsLog.Debug(logger, "Windows did not provide a current-user display name");
            return null;
        }

        var buffer = new char[checked((int)length)];
        fixed (char* bufferPointer = buffer)
        {
            if (GetUserNameEx(NameDisplay, bufferPointer, ref length))
            {
                var terminator = Array.IndexOf(buffer, '\0');
                return new string(buffer, 0, terminator >= 0 ? terminator : buffer.Length);
            }
        }

        WindowsLog.Warning(
            logger,
            $"Reading the current-user display name returned Windows error {Marshal.GetLastWin32Error()}");
        return null;
    }

    [LibraryImport(
        "secur32.dll",
        EntryPoint = "GetUserNameExW",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserNameEx(
        int nameFormat,
        char* userName,
        ref uint userNameSize);
}
