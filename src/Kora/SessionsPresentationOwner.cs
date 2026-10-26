using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kora.Core.Conversation;

namespace Kora;

// Owns the native selected-session presentation, not session/work authority.
internal sealed partial class SessionsPresentationOwner
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
    internal async Task<bool> PrepareShutdown()
    {
        try
        {
            if (!model.CanRead) { throw new InvalidOperationException("Finish the current native operation before voluntary shutdown."); }
            return await model.ConfirmUnsentDiscardAsync(all: true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            model.ReportComposerCloseFailure(exception);
            return false;
        }
    }

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
        surface.AddHandler(Button.ClickEvent, OnOriginalMessageReuse);
        surface.AddHandler(Button.ClickEvent, OnAppearanceSuggestion);
        surface.AddHandler(Button.ClickEvent, OnSpeechSummarySuggestion);
        surface.AddHandler(Button.ClickEvent, OnPlaybackVolumeSuggestion);
        surface.AddHandler(Button.ClickEvent, OnRegisteredSettingSuggestion);
        surface.AddHandler(Button.ClickEvent, OnModelMemoryProposal);
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "DispatcherTimer.Tick requires void; failures route to the session model's logged recovery status.")]
    private async void Refresh(object? sender, EventArgs args)
    {
        try { if (model.CanRefreshWork) { await model.RefreshWorkAsync(); } }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { model.ReportComposerCloseFailure(exception); }
    }

    private void Activated(object? sender, EventArgs args) => active = sender as Window;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Avalonia routed Click requires void; failures route to the session model's logged recovery status.")]
    private async void OpenConversationDetail(object? sender, RoutedEventArgs args)
    {
        if (revoked || args.Source is not Button { Name: "OpenConversationDetail" or "OpenAssistantConversationDetail", DataContext: ConversationMessage message } button
            || !model.CanOpenConversationDetails) { return; }
        args.Handled = true;
        try { await (DetailOperation = model.OpenConversationDetailAsync(message,
            assistant: string.Equals(button.Name, "OpenAssistantConversationDetail", StringComparison.Ordinal))); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { model.ReportComposerCloseFailure(exception); }
    }

    private void Closing(object? sender, WindowClosingEventArgs args)
    {
        if (NativeShutdownPolicy.RequiresImmediateRetirement(args.CloseReason))
        {
            revoked = true;
            model.Close();
            draftDialog?.Close();
            Release();
            return;
        }
        if (revoked) { return; }
        if (draftDialog is not null || closing) { args.Cancel = true; return; }
        if (surfaces.Count > 1 || !model.HasUnsentMessages) { return; }
        args.Cancel = true;
        if (!model.CanRead || sender is not Window surface) { return; }
        active = surface;
        CloseOperation = ProtectLastClose(surface);
    }

    private async Task ProtectLastClose(Window surface)
    {
        closing = true;
        var protectedDraft = false;
        try { protectedDraft = await model.ConfirmUnsentDiscardAsync(all: true); }
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
        surface.RemoveHandler(Button.ClickEvent, OnOriginalMessageReuse);
        surface.RemoveHandler(Button.ClickEvent, OnAppearanceSuggestion);
        surface.RemoveHandler(Button.ClickEvent, OnSpeechSummarySuggestion);
        surface.RemoveHandler(Button.ClickEvent, OnPlaybackVolumeSuggestion);
        surface.RemoveHandler(Button.ClickEvent, OnRegisteredSettingSuggestion);
        surface.RemoveHandler(Button.ClickEvent, OnModelMemoryProposal);
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
        var dialog = new Window { Title = "Discard unsent messages?", Width = 680, Height = 230 };
        var content = new StackPanel { Margin = new(18), Spacing = 14 };
        content.Children.Add(new TextBlock
        {
            Text = model.ComposerTarget + "\nUnsent messages are memory-only, separately held per session. Discard to continue closing/discarding, or Cancel to keep them. Switching sessions never discards or saves text. Nothing submits a message.",
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
