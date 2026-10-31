using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Hosting.SessionRouting;
using Kora.Core.Conversation.SessionRouting;

namespace Kora;

internal sealed class SessionRoutingWindow : Window
{
    private readonly SessionRoutingService routing;
    private readonly SessionRoutingReview review;
    private readonly Func<bool> admitted;
    private readonly Action<Exception> reportFailure;
    private readonly ComboBox destinations = new() { Name = "RoutingDestinations", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock acknowledgment = new() { Name = "RoutingAcknowledgment", TextWrapping = TextWrapping.Wrap };
    private readonly Button choose = new() { Name = "RoutingChoose", Content = "Review this destination" };
    private readonly Button submit = new() { Name = "RoutingSubmit", Content = "Submit original request to acknowledged destination", IsEnabled = false };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private SessionRoutingSelection? selection;
    private bool retired;
    private bool choosing;
    private long choiceRevision;

    internal SessionRoutingSelection? Result { get; private set; }
    internal Exception? Failure { get; private set; }
    internal Task ChoiceOperation { get; private set; } = Task.CompletedTask;

    internal SessionRoutingWindow(SessionRoutingService routing, SessionRoutingReview review,
        Func<bool> admitted, Action<Exception> reportFailure)
    {
        this.routing = routing;
        this.review = review;
        this.admitted = admitted;
        this.reportFailure = reportFailure;
        Title = "Review request destination";
        Width = 700; Height = 540; MinWidth = 500; MinHeight = 400;
        var root = new StackPanel { Margin = new(16), Spacing = 12 };
        root.Children.Add(new TextBlock
        {
            Text = review.Match switch
            {
                SessionRouteMatch.Clear => "One related Active conversation proposed. Review its name, correct the destination or choose New before submitting.",
                SessionRouteMatch.Ambiguous => "Several related Active conversations proposed. Choose one exact candidate or New. Nothing has been submitted.",
                SessionRouteMatch.NoMatch => "No clear relationship. New is the fresh-session fallback; deterministic correction is available.",
                _ => "Relatedness advice was not requested, is unqualified or is unavailable. Choose New or correct to an exact Active destination; no relationship was guessed.",
            },
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(new TextBlock { Text = "Original " + review.Origin + " request (unchanged):" });
        root.Children.Add(new ScrollViewer { MaxHeight = 150, Content = new TextBlock { Text = review.Body, TextWrapping = TextWrapping.Wrap } });
        AutomationProperties.SetName(destinations, "Exact host-presented destination or fresh New session");
        SetOptions(review.Candidates);
        destinations.SelectionChanged += (_, _) => ClearAcknowledgment();
        root.Children.Add(destinations);
        choose.Click += (_, _) => ChoiceOperation = ChooseAsync();
        root.Children.Add(choose);
        root.Children.Add(acknowledgment);
        var correct = new Button { Name = "RoutingCorrect", Content = "Correct destination / choose another Active session or New" };
        correct.Click += (_, _) =>
        {
            if (!Current() || choosing) { return; }
            ClearAcknowledgment();
            SetOptions(review.Snapshot.Descriptors);
        };
        root.Children.Add(correct);
        submit.Click += (_, _) =>
        {
            if (!Current() || choosing || selection is null) { return; }
            Result = selection;
            Close();
        };
        root.Children.Add(submit);
        var cancel = new Button { Name = "RoutingCancel", Content = "Cancel without submitting" };
        cancel.Click += (_, _) => Close();
        root.Children.Add(cancel);
        Content = new ScrollViewer { Content = root };
        timer.Tick += OnTick;
        Opened += (_, _) => { if (Current()) { timer.Start(); } else { Withdraw(); } };
        Closed += (_, _) => Retire();
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(); } };
    }

    private void SetOptions(IEnumerable<SessionRouteDescriptor> descriptors)
    {
        destinations.ItemsSource = descriptors.Select((item, index) => new Destination(
            (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + item.Name, item)).Append(new("New conversation (fresh session)", null)).ToArray();
        destinations.SelectedIndex = 0;
    }

    private bool Current() => !retired && admitted() && routing.IsCurrent(review);

    private async Task ChooseAsync()
    {
        if (!Current() || choosing || destinations.SelectedItem is not Destination destination) { return; }
        choosing = true;
        choose.IsEnabled = false;
        ClearAcknowledgment();
        var attempt = choiceRevision;
        try
        {
            var result = await routing.SelectAsync(review, destination.Descriptor?.Entry.Authority.SessionId, CancellationToken.None);
            if (!Current()) { Withdraw(); return; }
            if (attempt != choiceRevision) { return; }
            selection = result;
            acknowledgment.Text = result.Acknowledgment + " Correct it now or submit. Other sessions' unsent input, grants and provider state stay where they are.";
            submit.IsEnabled = true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure = exception;
            reportFailure(exception);
            Withdraw();
        }
        finally
        {
            choosing = false;
            choose.IsEnabled = Current();
        }
    }

    private void ClearAcknowledgment()
    {
        choiceRevision++;
        selection = null;
        acknowledgment.Text = string.Empty;
        submit.IsEnabled = false;
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
        ClearAcknowledgment();
        destinations.ItemsSource = null;
        Content = null;
    }

    private sealed record Destination(string Label, SessionRouteDescriptor? Descriptor)
    {
        public override string ToString() => Label;
    }
}
