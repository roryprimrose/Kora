using Kora.Core.Platform;

namespace Kora.Windows.Session;

internal interface IWindowsPrivacySource : IDisposable
{
    event EventHandler<WindowsPrivacySignalEventArgs>? Changed;

    WindowsPrivacySnapshot Read();
}
