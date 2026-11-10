namespace Kora;

internal sealed partial class SessionsManagementView
{
    private void BindListPageOrder(SessionsViewModel model) =>
        ClearListPageOrder.Click += (_, _) => model.ClearListPageOrder();
}
