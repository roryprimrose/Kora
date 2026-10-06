using Avalonia.Controls;

namespace Kora;

internal interface IDetailView
{
    event EventHandler? Closed;
    void ShowOwned(Window? owner);
    void Activate();
    void ClearAndClose();
}
