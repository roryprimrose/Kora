using Avalonia.Threading;

using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    private void OnDictationSourceChanging(HostId<SessionIdentity> session)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => OnDictationSourceChanging(session));
            return;
        }
        if (!disposed) { model?.RevokeDictationSource(session); }
    }
}
