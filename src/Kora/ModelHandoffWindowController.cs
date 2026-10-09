using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Kora.Application.Dependencies;
using Kora.Application.Infrastructure;
using Kora.Application.ViewModels;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class ModelHandoffWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly ModelHandoffPresentation source;
    private readonly ILogger<ModelHandoffWindowController> logger;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ModelHandoffWindow? window;
    private ModelHandoffReviewSession? review;
    private Func<bool> eligible = static () => false;
    private CancellationTokenSource? lifetime;
    private bool busy;
    private bool disposed;
    private int operations;
    private readonly List<(HostId<EvidenceIdentity> Id, CheckBox Control)> evidence = [];

    internal ModelHandoffWindowController(MainViewModel main, ModelHandoffPresentation source,
        ILogger<ModelHandoffWindowController> logger)
    {
        this.main = main;
        this.source = source;
        this.logger = logger;
        main.HandoffReviewRequested += OnRequested;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        timer.Tick += OnTick;
        main.BindHandoffReviewQuiescence(() => Volatile.Read(ref operations) == 0);
    }

    private void OnRequested(object? sender, EventArgs args)
    {
        if (disposed || !main.CanRevealPrivatePresentation) { return; }
        if (window is not null) { window.Activate(); return; }
        var hostEligible = main.CaptureHandoffPresentationEligibility();
        lifetime = new();
        window = new();
        var opened = window;
        eligible = () => ReferenceEquals(window, opened) && opened.IsVisible && hostEligible();
        opened.Closed += OnClosed;
        Bind("Inspect", Inspect);
        Bind("Approve", () => Decide(ModelHandoffDecision.Approve));
        Bind("Decline", () => Decide(ModelHandoffDecision.Decline));
        Bind("Cancel", () => Decide(ModelHandoffDecision.Cancel));
        Bind("Remove", Remove);
        opened.FindControl<ComboBox>("Offers")!.SelectionChanged += (_, _) => UpdateButtons();
        SetStatus("No pending qualified host offer has been observed. " + ModelHandoffPresentation.GateDisclosure);
        opened.Show();
        timer.Start();
        new AsyncCommand(() => Run(ReadPending), exception => ReportFailure(opened, exception)).Execute(null);
    }

    private void Bind(string name, Func<Task> action)
    {
        var opened = window!;
        opened.FindControl<Button>(name)!.Command = new AsyncCommand(() => Run(action),
            exception => ReportFailure(opened, exception));
    }

    private async Task Run(Func<Task> action)
    {
        if (busy || window is null || !eligible()) { return; }
        var opened = window;
        busy = true;
        Interlocked.Increment(ref operations);
        UpdateButtons();
        try { await action(); }
        finally
        {
            Interlocked.Decrement(ref operations);
            if (ReferenceEquals(window, opened)) { busy = false; UpdateButtons(); }
        }
    }

    private async Task ReadPending()
    {
        var opened = window;
        var pending = await source.ReadPending(RequestOrigin.LocalUi, eligible, lifetime!.Token);
        if (!ReferenceEquals(window, opened) || !eligible()) { return; }
        opened!.FindControl<ComboBox>("Offers")!.ItemsSource = pending.Select(offer => new OfferChoice(offer)).ToArray();
        SetStatus(pending.Count == 0 ? "Unavailable: no pending qualified host-issued offer. " + ModelHandoffPresentation.GateDisclosure
            : "Select one exact offer and read its complete envelope. No affirmative answer is selected. " + ModelHandoffPresentation.GateDisclosure);
        UpdateButtons();
    }

    private async Task Inspect()
    {
        var opened = window;
        if (review is null)
        {
            if (window!.FindControl<ComboBox>("Offers")!.SelectedItem is not OfferChoice choice) { return; }
            review = source.Open(choice.Offer, RequestOrigin.LocalUi, eligible);
        }
        if (review is null) { SetStatus("Stale or foreign offer; start a fresh review."); return; }
        var exact = review;
        await exact.Refresh(lifetime!.Token);
        if (ReferenceEquals(window, opened) && ReferenceEquals(review, exact)) { Present(); }
    }

    private async Task Decide(ModelHandoffDecision decision)
    {
        var opened = window;
        var exact = review!;
        await exact.Decide(decision, RequestOrigin.LocalUi, lifetime!.Token);
        if (ReferenceEquals(window, opened) && ReferenceEquals(review, exact)) { Present(); }
    }

    private async Task Remove()
    {
        var opened = window;
        var exact = review!;
        var ids = evidence.Where(item => item.Control.IsChecked == true).Select(item => item.Id).ToArray();
        if (ids.Length == 0) { SetStatus("Select exact evidence IDs to remove; no context was changed."); return; }
        await exact.Remove(ids, lifetime!.Token);
        if (ReferenceEquals(window, opened) && ReferenceEquals(review, exact)) { Present(); }
    }

    private void Present()
    {
        if (window is null || !eligible()) { return; }
        ClearContent();
        var state = review!;
        SetStatus($"{state.Outcome}; reason: {state.Reason}. " + ModelHandoffPresentation.GateDisclosure
            + (state.Outcome == ModelHandoffOutcome.Removed ? " Old offer/question retired. Read the reduced complete envelope; approval is not inherited." : string.Empty));
        if (state.Offer is not { } offer) { return; }
        window.FindControl<TextBlock>("Identity")!.Text =
            $"Offer {offer.Id:D} revision {offer.Revision.Value}; question {offer.Question.Key.QuestionId.Value:D} revision {offer.Question.Key.Revision.Value}\n"
            + $"Request {offer.Context.Request.RequestId.Value:D}; original origin {offer.Context.Request.Origin}\n"
            + $"Session {offer.Context.Request.SessionId.Value:D} generation {offer.Generation.Value}; task {offer.Context.Request.TaskId.Value:D} revision {offer.TaskRevision.Value}\n"
            + $"Policy revision {offer.Policy.Revision.Value}; control revision {offer.ControlRevision}; reason {offer.Reason}\n"
            + $"Destination {offer.Destination.Provider}; model {offer.Destination.Model.Value:D}; catalogue revision {offer.Destination.Revision.Value}\n"
            + $"Created {offer.Context.CreatedAt:O}; expires {offer.Context.ExpiresAt:O}; complete serialized UTF-8 bytes {offer.Context.Serialize().Length}/{ModelContextEnvelope.MaximumInputUtf8Bytes}; evidence {offer.Context.Evidence.Length}/{ModelContextEnvelope.MaximumEvidenceItems}";
        window.FindControl<TextBlock>("Preview")!.Text = state.Preview;
        foreach (var item in offer.Context.Evidence)
        {
            var control = new CheckBox { Content = $"Remove evidence {item.Id.Value:D}; revision {item.Revision.Value}; {item.Disclosure}", IsChecked = false };
            AutomationProperties.SetName(control, $"Remove exact evidence {item.Id.Value:D}");
            evidence.Add((item.Id, control));
            window.FindControl<StackPanel>("Evidence")!.Children.Add(control);
        }
    }

    private void UpdateButtons()
    {
        if (window is null) { return; }
        var admitted = !busy && eligible();
        window.FindControl<Button>("Inspect")!.IsEnabled = admitted && (review?.CanReview == true
            || review is null && window.FindControl<ComboBox>("Offers")!.SelectedItem is OfferChoice);
        window.FindControl<ComboBox>("Offers")!.IsEnabled = admitted && review is null;
        foreach (var name in new[] { "Approve", "Decline", "Cancel", "Remove" })
        {
            window.FindControl<Button>(name)!.IsEnabled = admitted && review?.CanReview == true
                && (!string.Equals(name, "Approve", StringComparison.Ordinal) || review.HasReviewed);
        }
    }

    private void OnTick(object? sender, EventArgs args)
    {
        if (!eligible()) { OnPrivacyClosed(this, EventArgs.Empty); return; }
        if (review?.CanReview == true && !busy)
        {
            var opened = window!;
            var exact = review;
            var token = lifetime!.Token;
            new AsyncCommand(() => Run(async () =>
            {
                await exact.Validate(token);
                if (ReferenceEquals(window, opened) && ReferenceEquals(review, exact) && exact.Offer is null) { Present(); }
            }), exception => ReportFailure(opened, exception)).Execute(null);
        }
    }

    private void SetStatus(string message)
    {
        if (window is not null) { window.FindControl<TextBlock>("Status")!.Text = message; }
    }

    private void ClearContent()
    {
        evidence.Clear();
        if (window is null) { return; }
        window.FindControl<TextBlock>("Identity")!.Text = string.Empty;
        window.FindControl<TextBlock>("Preview")!.Text = string.Empty;
        window.FindControl<StackPanel>("Evidence")!.Children.Clear();
    }

    private void ReportFailure(ModelHandoffWindow opened, Exception exception)
    {
        Failure(logger, exception.GetType().Name);
        if (ReferenceEquals(window, opened))
        {
            review?.Revoke();
            ClearContent();
            SetStatus("Review failed or cancelled; no successful approval claimed. Close and inspect a fresh exact host offer. " + ModelHandoffPresentation.GateDisclosure);
            UpdateButtons();
        }
    }

    private void OnPrivacyClosed(object? sender, EventArgs args)
    {
        source.RevokeAll();
        review?.Revoke();
        ClearContent();
        window?.Close();
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        timer.Stop();
        var closing = review;
        review = null;
        var cancellation = lifetime;
        lifetime = null;
        ClearContent();
        window = null;
        busy = false;
        Interlocked.Increment(ref operations);
        // Dismissal follows the same workflow, then deterministically retires the volatile lifetime.
        new AsyncCommand(async () =>
        {
            try
            {
                if (closing is not null)
                {
                    try { await closing.Dismiss(CancellationToken.None).ConfigureAwait(false); }
                    finally { await closing.DisposeAsync().ConfigureAwait(false); }
                }
            }
            finally
            {
                try
                {
                    if (cancellation is not null) { await cancellation.CancelAsync().ConfigureAwait(false); cancellation.Dispose(); }
                }
                finally { Interlocked.Decrement(ref operations); }
            }
        }, exception => Failure(logger, exception.GetType().Name)).Execute(null);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.HandoffReviewRequested -= OnRequested;
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        timer.Tick -= OnTick;
        OnPrivacyClosed(this, EventArgs.Empty);
    }

    private sealed record OfferChoice(ModelHandoffOffer Offer)
    {
        public override string ToString() => $"{Offer.Id:D} / {Offer.Context.Request.SessionId.Value:D}";
    }
}
