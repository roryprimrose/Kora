using System.Globalization;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

using Kora.Core.Storage;

namespace Kora;

internal sealed class NamedConversationChoiceView : UserControl
{
    internal Window Owner { get; }
    private readonly NativeViewLifetime presentation;
    internal event EventHandler? Closed;
    private readonly Func<bool> admitted;
    private readonly ComboBox choices = new() { Name = "NamedConversationChoices", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button acknowledge = new() { Name = "OpenNamedConversation", Content = "Open this exact conversation", IsEnabled = false };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool retired;
    private bool acknowledged;

    internal SessionWorkspaceEntry? Result { get; private set; }

    internal NamedConversationChoiceView(Window owner, SessionListSearchPage page, Func<bool> admitted)
    {
        Owner = owner;
        this.admitted = admitted;
        presentation = new(this, owner, () => !retired && admitted());
        presentation.PresentationChanged += (_, _) => { if (!presentation.IsPresented && !acknowledged) { Withdraw(); } };
        owner.Closed += OnOwnerClosed;
        var root = new StackPanel { Margin = new(16), Spacing = 12 };
        root.Children.Add(new TextBlock
        {
            Text = "Choose one exact conversation, then explicitly open it. No request will be sent. "
                + (page.Next is null ? "All matches in this bounded scan are shown."
                    : "This is a bounded match page; use Sessions metadata search for further matches."),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        choices.ItemsSource = page.Records.Select((entry, index) => new Choice(entry,
            (index + 1).ToString(CultureInfo.InvariantCulture) + ": " + entry.Metadata!.Name.Value
            + (entry.Authority.IsActive ? " — Active" : " — Done"))).ToArray();
        choices.SelectedIndex = -1;
        AutomationProperties.SetName(choices, "Exact currently permitted conversation choices");
        choices.SelectionChanged += (_, _) => acknowledge.IsEnabled = Current() && choices.SelectedItem is Choice;
        root.Children.Add(choices);
        acknowledge.Click += (_, _) =>
        {
            if (!Current()) { Withdraw(); return; }
            if (choices.SelectedItem is not Choice choice) { return; }
            acknowledged = true;
            Result = choice.Entry;
            Close();
        };
        root.Children.Add(acknowledge);
        var cancel = new Button { Name = "CancelNamedConversation", Content = "Cancel without opening" };
        cancel.Click += (_, _) => Withdraw();
        root.Children.Add(cancel);
        Content = new ScrollViewer { Content = root };
        timer.Tick += Validate;
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; Withdraw(); } };
    }

    internal void Show()
    {
        if (Owner is ResponseWindow primary)
        {
            primary.ShowSessions(expanded: true, preservePresentation: true);
            primary.ShowNativeReview(this, "Choose the conversation to read", this, admitted,
                shown => { if (!shown && !acknowledged) { Withdraw(); } });
        }
        else { Owner.Show(); }
        presentation.Refresh();
        if (Current()) { timer.Start(); choices.Focus(); }
        else { Withdraw(); }
    }

    internal void Hide() => Withdraw();
    private void OnOwnerClosed(object? sender, EventArgs args) => Withdraw();
    private bool Current() => !retired && presentation.IsPresented && admitted();
    private void Validate(object? sender, EventArgs args) { if (!Current()) { Withdraw(); } }
    internal void Withdraw()
    {
        Result = null;
        if (retired) { return; }
        Close();
    }

    private void Close()
    {
        if (retired) { return; }
        retired = true;
        timer.Stop();
        timer.Tick -= Validate;
        choices.ItemsSource = null;
        Content = null;
        Owner.Closed -= OnOwnerClosed;
        presentation.Dispose();
        IsVisible = false;
        Closed?.Invoke(this, EventArgs.Empty);
        if (Owner is ResponseWindow primary)
        { Dispatcher.UIThread.Post(() => primary.RemoveNativeReview(this)); }
        else if (Owner is NamedConversationChoiceWindow) { Owner.Close(); }
    }
    private sealed record Choice(SessionWorkspaceEntry Entry, string Label)
    {
        public override string ToString() => Label;
    }
}
