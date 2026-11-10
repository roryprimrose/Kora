using Avalonia.Controls;

namespace Kora;

internal sealed class AttachmentCatalogueWindow : Window
{
    internal AttachmentCatalogueView View { get; }
#pragma warning disable VSTHRD003 // Compatibility adapter exposes the exact operation owned by its view.
    internal Task Operation => View.Operation;
#pragma warning restore VSTHRD003

    internal AttachmentCatalogueWindow(AttachmentCatalogueViewModel model)
    {
        Title = "Retained session attachments - metadata only";
        Width = 1100; Height = 720; MinWidth = 850; MinHeight = 580;
        View = new(this, model, static () => true);
        Content = View;
        DataContext = model;
        NameScope.SetNameScope(this, NameScope.GetNameScope(View));
        Closed += (_, _) => View.Dispose();
    }

    internal void PrepareExactSession(Guid sessionId, Func<bool> admitted) => View.PrepareExactSession(sessionId, admitted);
}
