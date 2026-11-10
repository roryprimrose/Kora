using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora;

internal sealed partial class SessionsWindow : Window
{
    private readonly SessionsManagementView view;
#pragma warning disable VSTHRD003 // Compatibility adapter exposes only operations started by its owned view.
    internal Task ConversationOperation => view.ConversationOperation;
    internal Task ConversationCancellationOperation => view.ConversationCancellationOperation;
    internal Task ConversationNoticeOperation => view.ConversationNoticeOperation;
    internal Task StopAndDeleteOperation => view.StopAndDeleteOperation;
    internal Task MemoryBatchOperation => view.MemoryBatchOperation;
    internal Task MemoryOperation => view.MemoryOperation;
    internal Task SessionMemoryCopyOperation => view.SessionMemoryCopyOperation;
    internal Task MemoryContextOperation => view.MemoryContextOperation;
    internal Task ModelHistoryContextOperation => view.ModelHistoryContextOperation;
#pragma warning restore VSTHRD003
    internal void ClosePrivate() => view.ClosePrivate();
    internal T? FindControl<T>(string name) where T : Control => view.FindControl<T>(name);
    internal event EventHandler? AllWorkRequested;
    internal event EventHandler? NewRequestRequested;
    internal event EventHandler? CompactConversationRequested;
    internal event EventHandler? RetainedAttachmentsRequested;

    internal SessionsWindow(SessionsViewModel model, SessionsPresentationOwner? owner = null)
    {
        InitializeComponent();
        var presentation = owner ?? new(model);
        view = new(model, presentation, this);
        Content = view;
        NameScope.SetNameScope(this, NameScope.GetNameScope(view));
        DataContext = model;
        presentation.Attach(this);
        view.CloseRequested += (_, _) => Close();
        view.AllWorkRequested += (_, _) => AllWorkRequested?.Invoke(this, EventArgs.Empty);
        view.NewRequestRequested += (_, _) => NewRequestRequested?.Invoke(this, EventArgs.Empty);
        view.CompactConversationRequested += (_, _) => CompactConversationRequested?.Invoke(this, EventArgs.Empty);
        view.RetainedAttachmentsRequested += (_, _) => RetainedAttachmentsRequested?.Invoke(this, EventArgs.Empty);
        Closed += (_, _) => view.Dispose();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == DataContextProperty) { view.DataContext = DataContext; }
        };
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (!ReferenceEquals(args.Source, this)) { return; }
            var forwarded = new KeyEventArgs
            { RoutedEvent = KeyDownEvent, Key = args.Key, KeyModifiers = args.KeyModifiers };
            view.RaiseEvent(forwarded);
            args.Handled = forwarded.Handled;
        }, RoutingStrategies.Tunnel);
    }
}
