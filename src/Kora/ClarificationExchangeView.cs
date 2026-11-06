using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Automation;
using Avalonia.VisualTree;

using Kora.Core.Conversation;

namespace Kora;

internal sealed class ClarificationExchangeView : StackPanel
{
    public static readonly StyledProperty<SessionsViewModel?> WorkspaceProperty =
        AvaloniaProperty.Register<ClarificationExchangeView, SessionsViewModel?>(nameof(Workspace));
    public SessionsViewModel? Workspace
    {
        get => GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }
    private readonly Button show = new() { Name = "ShowClarificationExchange", Content = "Show clarification exchange" };
    private readonly Button more = new() { Name = "MoreClarificationExchange", Content = "More exchange records" };
    private readonly Button hide = new() { Name = "HideClarificationExchange", Content = "Hide exchange" };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Focusable = true };
    private readonly ItemsControl records = new()
    {
        Name = "ClarificationExchangeRecords",
        ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock
        { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Focusable = true, Margin = new(0, 4) }),
    };
    private SessionsViewModel? observed;
    private bool attached;
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public ClarificationExchangeView()
    {
        Spacing = 6;
        Children.Add(show);
        Children.Add(status);
        Children.Add(records);
        Children.Add(more);
        Children.Add(hide);
        AutomationProperties.SetName(show, "Read retained clarification for this exact original turn; no live question response or approval");
        AutomationProperties.SetName(more, "Read the next bounded page of the same clarification snapshot, including after zero matches");
        show.Click += async (_, _) => await (Operation = ReadAsync(next: false));
        more.Click += async (_, _) => await (Operation = ReadAsync(next: true));
        hide.Click += (_, _) => Workspace?.ClearClarificationExchange();
        DataContextChanged += (_, _) => Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        Observe();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        Observe();
        records.ItemsSource = null;
        status.Text = string.Empty;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WorkspaceProperty) { Observe(); }
    }

    private void Observe()
    {
        if (observed is not null) { observed.PropertyChanged -= OnModelChanged; }
        observed = attached ? Workspace : null;
        if (observed is not null) { observed.PropertyChanged += OnModelChanged; }
        Refresh();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionsViewModel.CurrentClarificationExchange)
            or nameof(SessionsViewModel.CanReadConversation)) { Refresh(); }
    }

    private void Refresh()
    {
        var exchange = Workspace?.CurrentClarificationExchange;
        var exact = DataContext is ConversationMessage message && ReferenceEquals(exchange?.Message, message);
        show.IsEnabled = Workspace?.CanReadConversation == true;
        status.Text = exact ? exchange!.Status : string.Empty;
        status.IsVisible = exact;
        records.ItemsSource = exact ? exchange!.Records : null;
        more.IsVisible = exact && exchange!.Page?.Next is not null;
        more.IsEnabled = show.IsEnabled;
        hide.IsVisible = exact;
    }

    private async Task ReadAsync(bool next)
    {
        if (Workspace is not { } model || DataContext is not ConversationMessage message) { return; }
        try { await model.ReadClarificationExchangeAsync(message, next); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { model.ReportDetailPresentationFailure(exception); }
    }
}
