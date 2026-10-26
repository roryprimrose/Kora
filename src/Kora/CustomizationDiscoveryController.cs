using System.ComponentModel;

using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.ViewModels;
using Kora.Core.Customizations;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class CustomizationDiscoveryController(MainViewModel main, CustomizationDiscoveryService metadata,
    SessionWorkspaceService workspace, SessionConversationService conversation, Func<bool> ownsHost,
    Func<long> ownerRevision, ILogger<CustomizationDiscoveryController> logger) : IAsyncDisposable
{
    private CustomizationDiscoveryWindow? window;
    private CustomizationDiscoverySnapshot? snapshot;
    private CancellationTokenSource? cancellation;
    private Task active = Task.CompletedTask;
    private Task cleanup = Task.CompletedTask;
    private long owner;
    private long call;
    private long inputRevision;
    private Guid? next;
    private RequestOrigin inputOrigin;
    private bool closed;

    internal void Bind()
    {
        main.CustomizationDiscoveryRequested += Open;
        main.PrivacyClosureRequested += PrivacyClosed;
        main.PropertyChanged += MainChanged;
        main.BindCustomizationDiscoveryShutdown(Shutdown);
    }

    private bool Eligible() => window is not null && ownsHost() && ownerRevision() == owner
        && main.CanRevealPrivatePresentation && main.CallPolicyRevision == call
        && main.IsSkillReviewCallEligible && !main.IsResponseInteractionPending;

    private async Task Open(CustomizationDiscoveryCommand command, RequestOrigin origin, Func<bool> admitted)
    {
        await CloseAndJoin();
        if (closed || !admitted()) { main.ReportHostInteractionFailure("Original discovery input expired. Nothing was read or shared."); return; }
        owner = ownerRevision();
        call = main.CallPolicyRevision;
        cancellation = new();
#pragma warning disable MA0147 // Native buttons use the reported and joined async event boundary below.
        window = new(Start);
#pragma warning restore MA0147
        window.Request.Text = command.Request;
        inputOrigin = origin;
        window.Request.PropertyChanged += RequestChanged;
        window.Sources.SelectionChanged += (_, _) => Retire();
        window.Targets.SelectionChanged += (_, _) => Retire();
        window.Closed += (_, _) => Close();
        window.Show();
        if (!Eligible() || !admitted()) { Close(); }
    }

    private void RequestChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property != Avalonia.Controls.TextBox.TextProperty) { return; }
        inputOrigin = RequestOrigin.LocalUi;
        Retire();
    }

    private void Retire()
    {
        inputRevision++;
        snapshot?.Retire();
        if (window is null) { return; }
        window.Preview.Text = window.Result.Text = string.Empty;
        window.Confirm.IsEnabled = false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native event boundary owns and joins RunReported, which reports failures without content.")]
    private async void Start(string action)
    {
        if (action is "Close") { Close(); return; }
        if (!active.IsCompleted) { return; }
        active = RunReported(action);
        await JoinActive();
    }

    private async Task RunReported(string action)
    {
        var opened = window;
        HostActivity? activity = null;
        try
        {
            if (opened is null || !Eligible()) { throw new InvalidOperationException("Private discovery is unavailable."); }
            var token = cancellation!.Token;
            var revision = inputRevision;
            bool Current() => Eligible() && ReferenceEquals(window, opened) && inputRevision == revision;
            activity = HostActivity.BeginRoot(HostRequest.Create(inputOrigin), HostActivityLayer.Desktop, HostOperation.Request);
            if (action is "Sources")
            {
                Retire();
                revision = inputRevision;
                var choices = await metadata.List(Current, token);
                if (!Current()) { return; }
                opened.Sources.ItemsSource = choices.Select(source => new SourceChoice(source)).ToArray();
                opened.Status.Text = "Only supported skill, agent and instruction choices were listed. Select exact sources; none is selected or enabled automatically.";
            }
            else if (action is "Targets" or "NextTargets")
            {
                Guid? after = action is "Targets" ? null : next ?? throw new InvalidOperationException("No next conversation page. Refresh first.");
                Retire();
                revision = inputRevision;
                var page = await workspace.ReadMetadataAsync(after, 25, token);
                if (!Current()) { return; }
                next = page.Next;
                opened.Targets.ItemsSource = page.Records.Where(entry => entry.Authority.IsActive).Select(entry => new TargetChoice(entry)).ToArray();
                opened.Status.Text = "Choose one exact existing Active conversation. No session was created, resumed or relatedness-routed.";
            }
            else if (action is "Review")
            {
                Retire();
                snapshot?.Dispose();
                snapshot = null;
                revision = inputRevision;
                var selected = opened.Sources.SelectedItems!.Cast<SourceChoice>().Select(choice => choice.Source).ToArray();
                var target = (opened.Targets.SelectedItem as TargetChoice)?.Entry
                    ?? throw new InvalidOperationException("Select one exact Active conversation.");
                var reviewed = await metadata.Review(selected, target, opened.Request.Text ?? string.Empty, inputOrigin, Current, token);
                if (!Current()) { reviewed.Dispose(); return; }
                snapshot = reviewed;
                opened.Preview.Text = CustomizationDiscoverySnapshot.Disclosure + "\n\nORIGINAL QUESTION\n"
                    + reviewed.OriginalRequest + "\n\nEXACT SHARED METADATA\n" + reviewed.Preview;
                opened.Confirm.IsEnabled = true;
                opened.Status.Text = "Review is not sharing or execution. Confirmation authorizes only ONE local metadata answer; changed sources require a fresh choice.";
            }
            else if (action is "Confirm")
            {
                var reviewed = snapshot ?? throw new InvalidOperationException("Choose and review a fresh metadata snapshot.");
                opened.Confirm.IsEnabled = false;
                var receipt = await conversation.SubmitCustomizationDiscovery(reviewed, Current, token);
                if (!Current()) { return; }
                opened.Result.Text = receipt.Message.AssistantResponse?.Body
                    ?? $"Retained terminal: {receipt.Message.Outcome} / {receipt.Message.Reason}. "
                        + (reviewed.SourceFailure?.Message ?? "No answer or invocation was fabricated.");
                opened.Status.Text = "Original request and genuine answer or terminal retained in the selected conversation. No customization was invoked.";
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            activity?.Complete(HostOperationOutcome.Cancelled);
            if (ReferenceEquals(window, opened) && opened is not null) { opened.Status.Text = "Discovery cancelled. Owned resources are being joined; nothing was activated."; }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            activity?.Complete(HostOperationOutcome.Failed);
            Failure(logger, exception.GetType().Name);
            if (ReferenceEquals(window, opened) && opened is not null)
            {
                opened.Confirm.IsEnabled = false;
                opened.Status.Text = exception is CustomizationDiscoverySourceException sourceFailure
                    ? sourceFailure.Message : exception is Kora.Core.Conversation.ConversationSubmissionUnconfirmedException { InnerException: { } cause }
                        ? "Conversation retention is unconfirmed (" + cause.GetType().Name + "). Inspect the exact conversation without repeating this request."
                        : "Discovery unavailable, stale, revoked or invalid (" + exception.GetType().Name + "). Choose fresh sources and inspect the exact conversation before retrying.";
            }
            main.ReportHostInteractionFailure("Customization metadata discovery did not confirm completion. No activation or automatic retry.");
        }
        finally { activity?.Dispose(); }
    }

    private void PrivacyClosed(object? sender, EventArgs args) => Close();
    private void MainChanged(object? sender, PropertyChangedEventArgs args) { if (!Eligible()) { Close(); } }
    private void Close()
    {
        var opened = window;
        if (opened is null) { return; }
        window = null;
        var retired = snapshot;
        snapshot = null;
        retired?.Retire();
        var lifetime = cancellation;
        cancellation = null;
        lifetime?.Cancel();
        opened.Clear();
        opened.Close();
        cleanup = Release(active, retired, lifetime);
    }

    private static async Task Release(Task running, CustomizationDiscoverySnapshot? retired, CancellationTokenSource? lifetime)
    {
#pragma warning disable VSTHRD003 // Join exactly this controller's captured operation before disposing its snapshot and cancellation owner.
        await running;
#pragma warning restore VSTHRD003
        retired?.Dispose();
        lifetime?.Dispose();
    }

    private async Task JoinActive()
    {
#pragma warning disable VSTHRD003 // Join exactly this controller's owned operation.
        await active;
#pragma warning restore VSTHRD003
    }

    internal Task WaitForOperation() => JoinActive();

    private async Task CloseAndJoin()
    {
        Close();
        await JoinActive();
#pragma warning disable VSTHRD003 // Join the exact source-release operation started by Close.
        await cleanup;
#pragma warning restore VSTHRD003
    }

    private async Task Shutdown()
    {
        closed = true;
        await CloseAndJoin();
        main.CustomizationDiscoveryRequested -= Open;
        main.PrivacyClosureRequested -= PrivacyClosed;
        main.PropertyChanged -= MainChanged;
        main.BindCustomizationDiscoveryShutdown(null);
    }

    public async ValueTask DisposeAsync() => await Shutdown();

    private sealed record SourceChoice(CustomizationSource Source)
    {
        public override string ToString() => $"{Source.Scope} / {Source.Kind} / {Source.Name} - PRIVATE source: {Source.Location}";
    }
    private sealed record TargetChoice(SessionWorkspaceEntry Entry)
    {
        public override string ToString() => Entry.Metadata?.Name.Value ?? $"Active conversation {Entry.Authority.SessionId.Value:D}";
    }
}
