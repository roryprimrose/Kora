using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Kora.Core.Presentation;

namespace Kora;

internal sealed partial class SessionInteractionView
{
    private readonly List<SessionDocumentTab> documentTabs = [];
    private bool selectingDocumentTab;
    private long documentTabRevision;
    private DetailContentReference? openingDocumentReference;
    internal Func<SessionContentTab, Func<bool>, Task>? ReopenHostDocumentAsync { get; set; }

    private bool AdmitDocumentTab(SessionDocumentTab candidate)
    {
        if (documentTabs.Any(tab => tab == candidate)) { return true; }
        if (documentTabs.Count == NativeDetailProfile.MaximumOpenViewers)
        {
            observedModel?.ReportDetailPresentationFailure(new InvalidOperationException(
                "Eight document tabs are already open. Close an exact tab before opening another item."));
            return false;
        }
        documentTabs.Add(candidate);
        return true;
    }

    private void ClearDocumentTabs()
    {
        Interlocked.Increment(ref documentTabRevision);
        openingDocumentReference = null;
        documentTabs.Clear();
        readingPositions.Clear();
        PresentDocumentTabs();
    }

    private void PresentDocumentTabs(DetailContentReference? selected = null)
    {
        selectingDocumentTab = true;
        try
        {
            DocumentTabs.Items.Clear();
            var session = observedModel?.SelectedSessionRecord?.Authority.SessionId;
            foreach (var tab in documentTabs.Where(tab => tab.Session == session))
            {
                var title = new TextBlock { Text = tab.Title, MaxWidth = 220, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
                var open = new Button { Name = "OpenDocumentTab", Content = title, Tag = tab };
                Avalonia.Automation.AutomationProperties.SetName(open, "Read exact document tab " + tab.Title);
                open.Click += OnOpenDocumentTab;
                var close = new Button { Name = "CloseDocumentTab", Content = "x", Tag = tab, Margin = new(6, 0, 0, 0) };
                Avalonia.Automation.AutomationProperties.SetName(close, "Close document tab " + tab.Title);
                close.Click += OnCloseDocumentTab;
                var header = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { open, close } };
                var item = new TabItem { Header = header, Tag = tab };
                DocumentTabs.Items.Add(item);
                if (selected == tab.Reference) { DocumentTabs.SelectedItem = item; }
            }
            if (selected is null) { DocumentTabs.SelectedIndex = -1; }
            DocumentTabs.IsVisible = DocumentTabs.ItemCount > 0;
            observedModel?.SetDocumentTabAvailability(DocumentTabs.ItemCount > 0);
        }
        finally { selectingDocumentTab = false; }
    }

    private void OnCloseDocumentTab(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: SessionDocumentTab tab }) { return; }
        args.Handled = true;
        Interlocked.Increment(ref documentTabRevision);
        if (activeDetails?.Reference == tab.Reference || activeEvidenceReference == tab.Reference
            || openingDocumentReference == tab.Reference) { HideDetails(); }
        documentTabs.Remove(tab);
        readingPositions.Remove(tab.Reference);
        PresentDocumentTabs(activeDetails?.Reference);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native tab selection boundary; failures are reported through the exact session presenter.")]
    private async void OnDocumentTabChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (selectingDocumentTab || !ReferenceEquals(args.Source, DocumentTabs)
            || DocumentTabs.SelectedItem is not TabItem { Tag: SessionDocumentTab tab })
        { return; }
        await (Operation = OpenDocumentTabAsync(tab));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native tab activation boundary; failures are reported through the exact session presenter.")]
    private async void OnOpenDocumentTab(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if (sender is Button { Tag: SessionDocumentTab tab, IsEffectivelyVisible: true })
        { await (Operation = OpenDocumentTabAsync(tab)); }
    }

    private async Task OpenDocumentTabAsync(SessionDocumentTab tab)
    {
        if (!documentTabs.Any(item => ReferenceEquals(item, tab))
            || activeDetails?.Reference == tab.Reference || activeEvidenceReference == tab.Reference
            || observedModel is not { } model || TopLevel.GetTopLevel(this) is not ResponseWindow window)
        { return; }
        HideDetails();
        openingDocumentReference = tab.Reference;
        var revision = Interlocked.Read(ref documentTabRevision);
        var sourceAdmission = model.CaptureNavigationEligibility();
        var presentationAdmission = window.CaptureSessionWorkspacePresentation();
        bool Admitted() => revision == Interlocked.Read(ref documentTabRevision)
            && sourceAdmission() && presentationAdmission();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            if (!Admitted()) { throw new OperationCanceledException("The original document tab activation was retired."); }
            PresentDocumentTabs(tab.Reference);
            DetailsHeading.Text = "Reading exact document";
            DetailsContent.Content = new TextBlock { Text = "Revalidating the original source. No retained body has been restored." };
            RevealDetails();
            if (tab is SessionContentTab { Origin: DetailContentOrigin.EmbeddedDocument
                or DetailContentOrigin.FinalizedResponse or DetailContentOrigin.RetrievedWebResult } hostTab)
            {
                await (ReopenHostDocumentAsync ?? throw new InvalidOperationException("The exact host document resolver is not composed."))(
                    hostTab, Admitted);
            }
            else { await model.ReopenDocumentTabAsync(tab, Admitted); }
            if (openingDocumentReference == tab.Reference && activeDetails is null && activeEvidenceReference is null)
            { HideDetails(); }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
            or InvalidDataException or ArgumentException or OperationCanceledException)
        {
            if (revision == Interlocked.Read(ref documentTabRevision)) { HideDetails(); }
            model.ReportDetailPresentationFailure(exception);
        }
        finally
        {
            if (revision == Interlocked.Read(ref documentTabRevision)) { openingDocumentReference = null; }
        }
    }

    private void PrepareDocumentReader(DetailContentReference reference)
    {
        if (openingDocumentReference is { } pending && pending != reference)
        {
            Interlocked.Increment(ref documentTabRevision);
            openingDocumentReference = null;
        }
        HideDetails(retireDocumentSelection: false);
    }
}
