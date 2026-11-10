namespace Kora;

public sealed partial class ResponseWindow
{
    internal SessionsManagementView? SessionManagement { get; private set; }
    internal bool IsManagementConversationPresented => SessionManagement?.IsConversationPresented == true;

    internal void BindSessionManagement(SessionsManagementView? view)
    {
        if (SessionManagement is { } old) { old.PresentationChanged -= OnManagementPresentationChanged; }
        SessionManagement = view;
        if (view is not null) { view.PresentationChanged += OnManagementPresentationChanged; }
        SessionView.NotifyManagementPresentationChanged();
    }

    private void OnManagementPresentationChanged(object? sender, EventArgs args) =>
        SessionView.NotifyManagementPresentationChanged();
}
