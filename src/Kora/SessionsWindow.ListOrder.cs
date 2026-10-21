namespace Kora;

internal sealed partial class SessionsWindow
{
    private void BindListPageOrder(SessionsViewModel model) =>
        ClearListPageOrder.Click += (_, _) => model.ClearListPageOrder();
}
