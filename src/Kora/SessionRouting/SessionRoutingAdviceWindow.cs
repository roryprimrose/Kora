using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Hosting.SessionRouting;

namespace Kora;

internal sealed class SessionRoutingAdviceWindow : Window
{
    private readonly Func<bool> admitted;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool retired;

    internal bool? Result { get; private set; }

    internal SessionRoutingAdviceWindow(SessionRoutingAdvice advice, Func<bool> admitted)
    {
        this.admitted = admitted;
        Title = "Review local destination advice";
        Width = 700; Height = 540; MinWidth = 500; MinHeight = 400;
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
        Opened += (_, _) => { if (Current()) { timer.Start(); } else { Withdraw(); } };
        Closed += (_, _) => Retire();
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(); } };
    }

    private bool Current() => !retired && admitted();

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
        Dispatcher.UIThread.Post(() => { Hide(); Close(); });
    }

    private void Retire()
    {
        retired = true;
        timer.Stop();
        timer.Tick -= OnTick;
        Content = null;
    }
}
