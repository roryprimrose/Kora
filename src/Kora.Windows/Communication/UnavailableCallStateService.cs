using Kora.Core.Communication;

namespace Kora.Windows.Communication;

public sealed class UnavailableCallStateService : ICallStateService
{
    public event EventHandler<CallStateChangedEventArgs>? StateChanged
    {
        add { }
        remove { }
    }

    public CallState CurrentState => CallState.Unavailable;
}
