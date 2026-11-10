using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Hosting.SessionRouting;

namespace Kora;

internal sealed class SessionRoutingAdviceView : UserControl, IDisposable
{
    internal Window Owner { get; }
    private readonly NativeViewLifetime presentation;
    private bool closed;
    internal event EventHandler? Closed;
    private readonly Func<bool> admitted;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool retired;

    internal bool? Result { get; private set; }

    internal SessionRoutingAdviceView(Window owner, SessionRoutingAdvice advice, Func<bool> admitted)
    {
        Owner = owner;
        this.admitted = admitted;
        presentation = new(this, owner, () => !retired && admitted());
        presentation.PresentationChanged += (_, _) =>
        { if (Owner is ResponseWindow && !presentation.IsPresented && !closed) { Withdraw(); } };
        owner.Opened += OnOwnerOpened;
        owner.Closed += OnOwnerClosed;
        var root = new StackPanel { Margin = new(16), Spacing = 12 };
        root.Children.Add(new TextBlock
        {
            Text = "Optional LOCAL destination advice only. Send the unchanged request and the listed Active conversation names "
                + "with their internal metadata evidence to one qualified local adviser. No history, answers, tools, unsent input, "
                + "memory or hosted provider. Unqualified advice stays unavailable. This does not select a destination, "
                + "create a task message or authorize the eventual answering model or its sources.",
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(new TextBlock { Text = "Original " + advice.Origin + " request (unchanged):" });
        root.Children.Add(new ScrollViewer
        {
            MaxHeight = 150,
            Content = new TextBlock { Name = "RoutingAdviceBody", Text = advice.Body, TextWrapping = TextWrapping.Wrap },
        });
        root.Children.Add(new TextBlock { Text = "Only this bounded permitted Active metadata page is offered:" });
        root.Children.Add(new ScrollViewer
        {
            MaxHeight = 150,
            Content = new TextBlock
            {
                Name = "RoutingAdviceNames",
                Text = string.Join("\n", advice.Snapshot.Descriptors.Select((item, index) =>
                    (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + item.Name)),
                TextWrapping = TextWrapping.Wrap,
            },
        });
        var allow = new Button { Name = "RoutingAdviceAllow", Content = "Allow this LOCAL advice once" };
        allow.Click += (_, _) => Decide(true);
        root.Children.Add(allow);
        var skip = new Button { Name = "RoutingAdviceSkip", Content = "Choose destination without advice" };
        skip.Click += (_, _) => Decide(false);
        root.Children.Add(skip);
        var cancel = new Button { Name = "RoutingAdviceCancel", Content = "Cancel request" };
        cancel.Click += (_, _) => Close();
        root.Children.Add(cancel);
        Content = new ScrollViewer { Content = root };
        timer.Tick += OnTick;
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(); } };
    }

    internal void Show()
    {
        if (Owner is not ResponseWindow primary) { Owner.Show(); return; }
        primary.ShowSessions(expanded: true, preservePresentation: true);
        primary.ShowNativeReview(this, "Review local destination advice", this, admitted,
            shown => { if (!shown && !closed) { Withdraw(); } });
        OnOwnerOpened(this, EventArgs.Empty);
    }
    private void OnOwnerOpened(object? sender, EventArgs args)
    {
        presentation.Refresh();
        if (Current()) { timer.Start(); } else { Withdraw(); }
    }
    private void OnOwnerClosed(object? sender, EventArgs args) => Close();
    public void Dispose() => Close();
    internal void ForwardRootKey(KeyEventArgs args)
    { if (args.Key == Key.Escape) { args.Handled = true; Close(); } }
    private bool Current() => !retired && (Owner is not ResponseWindow || presentation.IsPresented) && admitted();

    private void Decide(bool allow)
    {
        if (!Current()) { Withdraw(); return; }
        Result = allow;
        Close();
    }

    private void OnTick(object? sender, EventArgs args) { if (!Current()) { Withdraw(); } }

    internal void Withdraw()
    {
        Result = null;
        Retire();
        if (Owner is ResponseWindow) { Close(); }
        else { Dispatcher.UIThread.Post(() => { Owner.Hide(); Close(); }); }
    }

    private void Close()
    {
        if (closed) { return; }
        closed = true;
        Retire();
        Owner.Opened -= OnOwnerOpened;
        Owner.Closed -= OnOwnerClosed;
        presentation.Dispose();
        IsVisible = false;
        Closed?.Invoke(this, EventArgs.Empty);
        if (Owner is ResponseWindow primary)
        { Dispatcher.UIThread.Post(() => primary.RemoveNativeReview(this)); }
        else if (Owner is SessionRoutingAdviceWindow) { Owner.Close(); }
    }

    private void Retire()
    {
        retired = true;
        timer.Stop();
        timer.Tick -= OnTick;
        Content = null;
    }
}
