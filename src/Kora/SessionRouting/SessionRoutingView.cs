using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Hosting.SessionRouting;
using Kora.Core.Conversation.SessionRouting;
using static Kora.Application.Hosting.SessionRouting.SessionRoutingVoiceReply;

namespace Kora;

internal sealed class SessionRoutingView : UserControl, IDisposable
{
    internal Window Owner { get; }
    private readonly NativeViewLifetime presentation;
    internal event EventHandler? Closed;
    private bool closed;
    private readonly SessionRoutingService routing;
    private readonly SessionRoutingReview review;
    private readonly Func<bool> admitted;
    private readonly Action<Exception> reportFailure;
    private readonly ComboBox destinations = new() { Name = "RoutingDestinations", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock acknowledgment = new() { Name = "RoutingAcknowledgment", TextWrapping = TextWrapping.Wrap };
    private readonly Button choose = new() { Name = "RoutingChoose", Content = "Review this destination" };
    private readonly Button submit = new() { Name = "RoutingSubmit", Content = "Submit original request to acknowledged destination", IsEnabled = false };
    private readonly Button voice = new() { Name = "RoutingAnswerByVoice", Content = "Answer by voice" };
    private readonly TextBlock voiceStatus = new() { Name = "RoutingVoiceStatus", Text = "Microphone closed. Voice replies require a separate explicit gesture for each question.", TextWrapping = TextWrapping.Wrap };
    private readonly Func<SessionRoutingReview, IEnumerable<SessionRouteDescriptor>, SessionRoutingSelection?, Func<bool>, SessionRoutingVoiceReply?>? createVoiceReply;
    private readonly Action<SessionRoutingVoiceReply>? releaseVoiceReply;
    private SessionRoutingVoiceReply? voiceReply;
    private Destination[] presented = [];
    private bool voiceBusy;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private SessionRoutingSelection? selection;
    private bool retired;
    private bool choosing;
    private long choiceRevision;

    internal SessionRoutingSelection? Result { get; private set; }
    internal Exception? Failure { get; private set; }
    internal Task ChoiceOperation { get; private set; } = Task.CompletedTask;
    internal Task VoiceOperation { get; private set; } = Task.CompletedTask;

    internal SessionRoutingView(Window owner, SessionRoutingService routing, SessionRoutingReview review,
        Func<bool> admitted, Action<Exception> reportFailure,
        Func<SessionRoutingReview, IEnumerable<SessionRouteDescriptor>, SessionRoutingSelection?, Func<bool>, SessionRoutingVoiceReply?>? createVoiceReply = null,
        Action<SessionRoutingVoiceReply>? releaseVoiceReply = null)
    {
        Owner = owner;
        this.routing = routing;
        this.review = review;
        this.admitted = admitted;
        this.reportFailure = reportFailure;
        this.createVoiceReply = createVoiceReply;
        this.releaseVoiceReply = releaseVoiceReply;
        presentation = new(this, owner, () => !retired && admitted());
        presentation.PresentationChanged += (_, _) =>
        { if (Owner is ResponseWindow && !presentation.IsPresented && !closed) { Withdraw(); } };
        owner.Opened += OnOwnerOpened;
        owner.Closed += OnOwnerClosed;
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
        voice.Click += (_, _) =>
        {
            if (Current() && !voiceBusy && !choosing) { VoiceOperation = AnswerByVoiceAsync(); }
        };
        root.Children.Add(voice);
        root.Children.Add(voiceStatus);
        var cancel = new Button { Name = "RoutingCancel", Content = "Cancel without submitting" };
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
        primary.ShowNativeReview(this, "Review request destination", this, admitted,
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

    private void SetOptions(IEnumerable<SessionRouteDescriptor> descriptors)
    {
        presented = descriptors.Select((item, index) => new Destination(
            (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + item.Name, item)).Append(new("New conversation (fresh session)", null)).ToArray();
        destinations.ItemsSource = presented;
        destinations.SelectedIndex = 0;
    }

    private bool Current() => !retired && (Owner is not ResponseWindow || presentation.IsPresented)
        && admitted() && routing.IsCurrent(review);

    private async Task ChooseAsync()
    {
        if (!Current() || choosing || destinations.SelectedItem is not Destination destination) { return; }
        choosing = true;
        choose.IsEnabled = false;
        ClearAcknowledgment();
        var attempt = choiceRevision;
        try
        {
            await RetireVoiceReplyAsync();
            if (!Current() || attempt != choiceRevision) { return; }
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
        voiceReply?.Cancel();
        choiceRevision++;
        selection = null;
        acknowledgment.Text = string.Empty;
        submit.IsEnabled = false;
    }

    private async Task AnswerByVoiceAsync()
    {
        voiceBusy = true;
        voice.IsEnabled = false;
        var revision = choiceRevision;
        var acknowledged = selection;
        bool Exact() => Current() && revision == choiceRevision && ReferenceEquals(selection, acknowledged);
        try
        {
            var reply = createVoiceReply?.Invoke(review, presented.Where(item => item.Descriptor is not null).Select(item => item.Descriptor!),
                acknowledged, Exact);
            if (reply is null)
            {
                voiceStatus.Text = "Voice replies are unavailable. Microphone closed; use the native destination and confirmation controls.";
                return;
            }
            voiceReply = reply;
            reply.Changed += VoiceChanged;
            SessionRoutingVoiceAnswer? answer;
            try
            {
                answer = await reply.StartAsync();
                voiceStatus.Text = reply.Status;
            }
            finally
            {
                await reply.DisposeAsync();
                reply.Changed -= VoiceChanged;
                releaseVoiceReply?.Invoke(reply);
                if (ReferenceEquals(voiceReply, reply)) { voiceReply = null; }
            }
            if (!Exact()) { if (!Current()) { Withdraw(); } return; }
            if (answer is null) { return; }
            switch (answer.Choice)
            {
                case SessionRoutingVoiceChoice.Destination:
                    destinations.SelectedItem = presented.Single(item => ReferenceEquals(item.Descriptor, answer.Destination));
                    await ChooseAsync();
                    break;
                case SessionRoutingVoiceChoice.New:
                    destinations.SelectedItem = presented.Single(item => item.Descriptor is null);
                    await ChooseAsync();
                    break;
                case SessionRoutingVoiceChoice.Submit:
                    if (acknowledged is null) { throw new InvalidOperationException("Voice submission requires the exact acknowledged selection."); }
                    Result = acknowledged;
                    Close();
                    break;
                case SessionRoutingVoiceChoice.Correct:
                    ClearAcknowledgment();
                    SetOptions(review.Snapshot.Descriptors);
                    break;
                case SessionRoutingVoiceChoice.Cancel:
                    Close();
                    break;
                default:
                    throw new InvalidOperationException("The closed routing question returned an unsupported answer.");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure = exception;
            reportFailure(exception);
            Withdraw();
        }
        finally
        {
            voiceBusy = false;
            voice.IsEnabled = Current();
        }
    }

    private void VoiceChanged(object? sender, EventArgs args) => Dispatcher.UIThread.Post(() =>
    {
        if (!retired && voiceReply is { } reply && ReferenceEquals(sender, reply)) { voiceStatus.Text = reply.Status; }
    });

    private async Task RetireVoiceReplyAsync()
    {
        if (voiceReply is { } reply) { await reply.DisposeAsync(); }
    }

    internal async Task JoinAsync()
    {
#pragma warning disable VSTHRD003 // Join exact native choice and voice ownership before the controller can submit or shut down.
        await Task.WhenAll(ChoiceOperation, VoiceOperation);
#pragma warning restore VSTHRD003
        await RetireVoiceReplyAsync();
    }

    private void OnTick(object? sender, EventArgs args)
    {
        voiceReply?.Revalidate();
        if (!Current()) { Withdraw(); }
    }

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
        else if (Owner is SessionRoutingWindow) { Owner.Close(); }
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
