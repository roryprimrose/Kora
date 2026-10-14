using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kora.Core.Conversation;

namespace Kora;

// Owns the native selected-session presentation, not session/work authority.
internal sealed class SessionsPresentationOwner
{
    private readonly SessionsViewModel model;
    private readonly EventHandler<RoutedEventArgs> conversationDetailClick;
    private readonly List<Window> surfaces = [];
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(5) };
    private Window? active;
    private Window? draftDialog;
    private bool closing;
    private bool revoked;
    internal Task CloseOperation { get; private set; } = Task.CompletedTask;
    internal Task DetailOperation { get; private set; } = Task.CompletedTask;

    internal SessionsPresentationOwner(SessionsViewModel model)
    {
        this.model = model;
#pragma warning disable MA0147 // Avalonia routed-event boundary; the model operation handles and reports failures.
        conversationDetailClick = OpenConversationDetail;
#pragma warning restore MA0147
        model.DecideComposerDraft = ChooseDraft;
        model.SetProviderSurfaceOpen(false);
        refresh.Tick += Refresh;
    }

    internal void Attach(Window surface)
    {
        if (revoked) { throw new InvalidOperationException("The selected-session presentation was closed."); }
        surfaces.Add(surface);
        surface.Activated += Activated;
        surface.Closing += Closing;
        surface.Closed += Closed;
        surface.AddHandler(Button.ClickEvent, conversationDetailClick);
        refresh.Start();
    }

    internal void Revoke()
    {
        if (revoked) { return; }
        revoked = true;
        draftDialog?.Close();
        model.Close();
        foreach (var surface in surfaces.ToArray()) { surface.Close(); }
        Release();
    }

    private async void Refresh(object? sender, EventArgs args)
    {
        if (model.CanRefreshWork) { await model.RefreshWorkAsync(); }
    }

    private void Activated(object? sender, EventArgs args) => active = sender as Window;

    private async void OpenConversationDetail(object? sender, RoutedEventArgs args)
    {
        if (revoked || args.Source is not Button { Name: "OpenConversationDetail", DataContext: ConversationMessage message }
            || !model.CanOpenConversationDetails) { return; }
        args.Handled = true;
        await (DetailOperation = model.OpenConversationDetailAsync(message));
    }

    private void Closing(object? sender, WindowClosingEventArgs args)
    {
        if (revoked) { return; }
        if (draftDialog is not null || closing) { args.Cancel = true; return; }
        if (surfaces.Count > 1 || !model.ComposerDirty) { return; }
        args.Cancel = true;
        if (!model.CanRead || sender is not Window surface) { return; }
        active = surface;
        CloseOperation = ProtectLastClose(surface);
    }

    private async Task ProtectLastClose(Window surface)
    {
        closing = true;
        var protectedDraft = false;
        try { protectedDraft = await model.ProtectComposerAsync(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { model.ReportComposerCloseFailure(exception); }
        finally { closing = false; }
        if (!revoked && protectedDraft && surfaces.Contains(surface)) { surface.Close(); }
    }

    private void Closed(object? sender, EventArgs args)
    {
        if (sender is not Window surface) { return; }
        surface.Activated -= Activated;
        surface.Closing -= Closing;
        surface.Closed -= Closed;
        surface.RemoveHandler(Button.ClickEvent, conversationDetailClick);
        surfaces.Remove(surface);
        if (ReferenceEquals(active, surface)) { active = null; }
        if (surfaces.Count != 0) { return; }
        revoked = true;
        model.Close();
        Release();
    }

    private void Release()
    {
        refresh.Stop();
        refresh.Tick -= Refresh;
        model.DecideComposerDraft = null;
        active = null;
    }

    private async Task<ComposerDraftDecision> ChooseDraft()
    {
        var owner = active is { IsVisible: true } ? active : surfaces.FirstOrDefault(surface => surface.IsVisible);
        if (revoked || owner is null || draftDialog is not null) { return ComposerDraftDecision.Cancel; }
        var dialog = new Window { Title = "Unsaved exact-session conversation draft", Width = 680, Height = 230 };
        var content = new StackPanel { Margin = new(18), Spacing = 14 };
        content.Children.Add(new TextBlock
        {
            Text = model.ComposerTarget + "\nSave explicitly, discard the shared memory-only buffer, or cancel switching/closing. Nothing submits a message.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        foreach (var decision in Enum.GetValues<ComposerDraftDecision>())
        {
            var button = new Button { Content = decision.ToString(), Name = "Draft" + decision };
            button.Click += (_, _) => dialog.Close(decision);
            buttons.Children.Add(button);
        }
        content.Children.Add(buttons);
        dialog.Content = content;
        draftDialog = dialog;
        try { return await dialog.ShowDialog<ComposerDraftDecision?>(owner) ?? ComposerDraftDecision.Cancel; }
        finally
        {
            content.Children.Clear();
            if (ReferenceEquals(draftDialog, dialog)) { draftDialog = null; }
        }
    }
}
