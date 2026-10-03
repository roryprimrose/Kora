namespace Kora.Core.Communication;

public interface ICallStateService
{
    event EventHandler<CallStateChangedEventArgs>? StateChanged;

    CallState CurrentState { get; }
}
