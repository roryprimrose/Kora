using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Kora;

internal sealed class NativeReviewTabsView : UserControl, IDisposable
{
    private readonly TabControl tabs = new();
    private readonly Dictionary<Control, Entry> entries = [];
    private readonly NativeViewLifetime lifetime;
    private bool updating;
    private bool disposed;
    internal event EventHandler? EntriesChanged;
    internal event EventHandler? ReturnRequested;
    internal int Count => entries.Count;
    internal void RefreshPresentation()
    {
        lifetime.Refresh();
        UpdatePresentation();
    }

    internal NativeReviewTabsView(Window owner)
    {
        var back = new Button { Content = "Return to main view", Name = "ReturnFromNativeReview" };
        Avalonia.Automation.AutomationProperties.SetName(back, "Hide native review pane without approving, answering or cancelling");
        back.Click += (_, _) => ReturnRequested?.Invoke(this, EventArgs.Empty);
        var layout = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 8 };
        layout.Children.Add(back);
        Grid.SetRow(tabs, 1);
        layout.Children.Add(tabs);
        Content = layout;
        lifetime = new(this, owner, () => !disposed);
        lifetime.PresentationChanged += (_, _) => UpdatePresentation();
        lifetime.ActivationChanged += (_, _) => UpdatePresentation();
        tabs.SelectionChanged += (_, args) =>
        {
            if (!updating && ReferenceEquals(args.Source, tabs))
            {
                if (tabs.SelectedItem is TabItem { Tag: Entry selected }) { selected.NeedsReveal = false; }
                UpdatePresentation();
            }
        };
        Avalonia.Automation.AutomationProperties.SetName(tabs, "Original native review tabs; not generated action or approval controls");
    }

    internal void ShowReview(Control owner, string title, Control body, Func<bool> admitted, Action<bool> presented)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!admitted()) { throw new InvalidOperationException("The original native review source is no longer presented."); }
        if (!entries.TryGetValue(owner, out var entry))
        {
            if (entries.Count == 8) { throw new InvalidOperationException("Eight native review tabs are already open. Hide an exact review before opening another."); }
            if (body.Parent is Panel parent) { parent.Children.Remove(body); }
            else if (body.Parent is ContentControl content && ReferenceEquals(content.Content, body)) { content.Content = null; }
            else if (body.Parent is not null) { throw new InvalidOperationException("The exact review control has an unsupported owner."); }
            entry = new(body, admitted, presented);
            var open = new Button { Content = title };
            entry.OpenButton = open;
            Avalonia.Automation.AutomationProperties.SetName(open, "Show original review " + title);
            open.Click += (_, args) =>
            {
                args.Handled = true;
                entry.NeedsReveal = false;
                tabs.SelectedItem = entry.Tab;
                UpdatePresentation();
            };
            var close = new Button { Content = "x", Name = "HideNativeReview" };
            Avalonia.Automation.AutomationProperties.SetName(close, "Hide original review " + title + " without answering or approving");
            close.Click += (_, args) => { args.Handled = true; RemoveReview(owner); };
            entry.Tab = new TabItem
            {
                Header = new StackPanel
                { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { open, close } },
                Content = body, Tag = entry,
            };
            entries.Add(owner, entry);
            updating = true;
            try { tabs.Items.Add(entry.Tab); }
            finally { updating = false; }
        }
        else
        {
            if (!ReferenceEquals(entry.Body, body))
            { throw new InvalidOperationException("The original review owner cannot substitute another review control."); }
            if (entry.Presented) { entry.Presented = false; entry.Publish(false); }
            entry.Admitted = admitted;
            entry.Publish = presented;
            entry.OpenButton.Content = title;
            Avalonia.Automation.AutomationProperties.SetName(entry.OpenButton, "Show original review " + title);
        }
        entry.NeedsReveal = false;
        tabs.SelectedItem = entry.Tab;
        EntriesChanged?.Invoke(this, EventArgs.Empty);
        UpdatePresentation();
    }

    internal void RemoveReview(Control owner)
    {
        if (!entries.Remove(owner, out var entry)) { return; }
        if (entry.Presented) { entry.Presented = false; entry.Publish(false); }
        updating = true;
        try { tabs.Items.Remove(entry.Tab); entry.Tab.Content = null; }
        finally { updating = false; }
        EntriesChanged?.Invoke(this, EventArgs.Empty);
        UpdatePresentation();
    }

    internal void UpdateTitle(Control owner, string title)
    {
        if (!entries.TryGetValue(owner, out var entry)) { return; }
        entry.OpenButton.Content = title;
        Avalonia.Automation.AutomationProperties.SetName(entry.OpenButton, "Show original review " + title);
    }

    private void UpdatePresentation()
    {
        foreach (var entry in entries.Values.ToArray())
        {
            var current = lifetime.IsActive && ReferenceEquals(tabs.SelectedItem, entry.Tab)
                && !entry.NeedsReveal && entry.Admitted();
            if (entry.Presented == current) { continue; }
            entry.Presented = current;
            if (!current) { entry.NeedsReveal = true; }
            entry.Publish(current);
        }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        foreach (var owner in entries.Keys.ToArray()) { RemoveReview(owner); }
        lifetime.Dispose();
        Content = null;
    }

    private sealed class Entry(Control body, Func<bool> admitted, Action<bool> publish)
    {
        internal Control Body { get; } = body;
        internal Func<bool> Admitted { get; set; } = admitted;
        internal Action<bool> Publish { get; set; } = publish;
        internal Button OpenButton { get; set; } = new();
        internal TabItem Tab { get; set; } = new();
        internal bool Presented { get; set; }
        internal bool NeedsReveal { get; set; }
    }
}
