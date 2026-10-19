using System.ComponentModel;
using Avalonia.Threading;
using Kora.Application.Definitions;
using Kora.Application.ViewModels;
using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class PromptParameterController(
    MainViewModel main, ArtifactCatalogue catalogue, PromptRequestConsumer consumer,
    PromptConversationSubmission submission,
    Func<bool> ownsHost, Func<long> ownershipRevision, ILogger<PromptParameterController> logger) : IDisposable, IAsyncDisposable
{
    private PromptParameterWindow? window;
    private PromptParameterDraft? draft;
    private CancellationTokenSource? lifetime;
    private long ownership;
    private bool busy;
    private bool disposed;
    private bool closing;
    private Task activeOperation = Task.CompletedTask;
    private SessionWorkspaceEntry? target;
    private Guid? nextTarget;
    private long targetRevision;
    private long reviewRevision;
    private PreparedPromptConversation? reviewed;
    private bool submissionHeld;
#pragma warning disable VSTHRD003 // Exposes this controller's owned operation for asynchronous observation without duplicating submission.
    internal Task ActiveOperation => activeOperation;
#pragma warning restore VSTHRD003

    internal void Bind()
    {
        main.PromptPreparationRequested += Open;
        main.PromptPreparationSuperseded += OnPrivacyClosed;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.PropertyChanged += OnInputChanged;
        catalogue.Changed += OnCatalogueChanged;
        catalogue.ReloadFailed += OnReloadFailed;
        main.BindPromptPreparationShutdown(CloseAndWait);
    }

    private Task Open(PromptParameterDraft proposed)
    {
        Close();
        if (disposed || closing || !ownsHost() || !proposed.IsCurrent)
        {
            proposed.Dispose();
            main.ReportHostInteractionFailure("Prompt preparation requires current private ownership and exact enabled source.");
            return Task.CompletedTask;
        }
        ownership = ownershipRevision();
        draft = proposed;
        lifetime = new();
        window = new(proposed, Edit, action =>
        {
            if (!busy) { activeOperation = Run(action); }
        }, SelectTarget);
        window.Deactivated += (_, _) => RetireReview();
        window.Activated += (_, _) => EnableReview();
        var opened = window;
        window.Closed += (_, _) => { if (ReferenceEquals(window, opened)) { Clear(); } };
        window.Show();
        return Task.CompletedTask;
    }

    private bool Eligible(PromptParameterDraft captured) => !disposed && ownsHost()
        && ownershipRevision() == ownership && ReferenceEquals(draft, captured) && captured.IsCurrent;

    private void Edit(string name, string value)
    {
        RetireReview();
        window?.RetirePreview();
        if (draft is null) { return; }
        try { draft.SetValue(name, value); window!.Status.Text = "Input changed. Preview the whole expanded request again."; }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        { window!.Status.Text = exception.Message; }
    }

    private void SelectTarget(SessionWorkspaceEntry? selected)
    {
        RetireReview();
        Volatile.Write(ref target, selected);
        Interlocked.Increment(ref targetRevision);
        EnableReview();
    }

    private void RetireReview()
    {
        Interlocked.Increment(ref reviewRevision);
        Volatile.Write(ref reviewed, null);
        window?.RetireReview();
    }

    private void EnableReview()
    {
        if (window is { } opened)
        { opened.ReviewSubmit.IsEnabled = !busy && !submissionHeld && draft?.CanUse == true && target?.Authority.IsActive == true; }
    }

    private async Task Run(string action)
    {
        if (busy || draft is not { } captured || window is not { } opened || lifetime is null) { return; }
        var token = lifetime.Token;
        busy = true;
        opened.ConfirmSubmit.IsEnabled = false;
        opened.ReviewSubmit.IsEnabled = false;
        try
        {
            if (!Eligible(captured)) { Close(); return; }
            if (submissionHeld) { throw new InvalidOperationException("Submission is held. Inspect its exact receipts; no replay from this form."); }
            if (string.Equals(action, "Preview", StringComparison.Ordinal))
            {
                RetireReview();
                opened.RetirePreview();
                var expanded = await captured.Preview(main.IsPromptSourceCurrent, token);
                if (!Eligible(captured)) { return; }
                opened.Expanded.Text = expanded.UserRequest;
                opened.Use.IsEnabled = captured.CanUse;
                opened.Status.Text = $"Complete expanded USER REQUEST; digest {expanded.Digest}. Preview is not model/tool approval. Use is separate.";
            }
            else if (string.Equals(action, "Use", StringComparison.Ordinal))
            {
                RetireReview();
                var expanded = await captured.CaptureUse(main.IsPromptSourceCurrent, token);
                bool Current() => Eligible(captured) && captured.IsUseCurrent(expanded);
                var result = await consumer.Use(expanded, Current, token);
                if (!Current()) { return; }
                main.ReportPromptUse(result);
                opened.Status.Text = $"Generation unavailable: {result.Outcome} / {result.Reason}. No model or action started; inputs remain ephemeral.";
            }
            else if (action is "RefreshTargets" or "NextTargets")
            {
                RetireReview();
                Volatile.Write(ref target, null);
                Interlocked.Increment(ref targetRevision);
                opened.Targets.SelectedItem = null;
                opened.Targets.ItemsSource = null;
                var after = string.Equals(action, "NextTargets", StringComparison.Ordinal)
                    ? nextTarget ?? throw new InvalidOperationException("Refresh session metadata first.") : (Guid?)null;
                nextTarget = null;
                opened.NextTargets.IsEnabled = false;
                var page = await submission.ReadTargets(after, () => Eligible(captured), token);
                if (!Eligible(captured)) { return; }
                nextTarget = page.Next;
                opened.Targets.ItemsSource = page.Records.Where(entry => entry.Authority.IsActive)
                    .Select(entry => new PromptConversationTarget(entry)).ToArray();
                opened.NextTargets.IsEnabled = nextTarget is not null;
                opened.Status.Text = "Passive metadata observation only; explicitly select an Active immutable target. "
                    + (nextTarget is null ? "End of this observation." : "More metadata requires Next.");
            }
            else if (string.Equals(action, "CancelReview", StringComparison.Ordinal))
            {
                RetireReview();
                opened.Status.Text = "Retention review cancelled. Nothing submitted; eligible parameter values remain memory-only.";
            }
            else if (string.Equals(action, "ReviewSubmit", StringComparison.Ordinal))
            {
                if (submissionHeld) { throw new InvalidOperationException("Inspect the exact submission receipt; this form cannot retry."); }
                RetireReview();
                var selected = target ?? throw new InvalidOperationException("Explicitly select one existing Active session.");
                var selection = Interlocked.Read(ref targetRevision);
                var review = Interlocked.Read(ref reviewRevision);
                bool Current() => Eligible(captured) && Volatile.Read(ref target) == selected
                    && Interlocked.Read(ref targetRevision) == selection && Interlocked.Read(ref reviewRevision) == review && !submissionHeld;
                var prepared = await submission.Prepare(captured, selected, main.IsPromptSourceCurrent, Current, token);
                if (!Current()) { return; }
                Volatile.Write(ref reviewed, prepared);
                opened.RetentionReview.Text = $"Exact immutable target: {new PromptConversationTarget(selected)}\n"
                    + $"Observed metadata UTC: {DateTimeOffset.UtcNow:O}\nOwned source: {prepared.Expanded.Source.Id} | kind {prepared.Expanded.Source.Kind}\n"
                    + $"Original native revision: {prepared.Expanded.Source.OwnedInstructionRevision}\nOriginal source digest: {prepared.Expanded.Source.Digest}\n"
                    + $"Expanded digest: {prepared.Expanded.Digest} | value revision {prepared.Expanded.ValueRevision}\n"
                    + "CONFIRMATION: The entire expanded text below will persist as one USER conversation message in this exact session's "
                    + "private readable SQLite authority (not an encryption claim), with a fresh host task and required audit. Existing retention applies. "
                    + "Local best-effort reject-only credential screening cannot guarantee secret-free text. No Save anyway or silent redaction. "
                    + "Source provenance shown here is volatile; only the approved expanded user body is retained. No hidden form values/template/source path are saved. "
                    + "Generation is unavailable; no model, network, authentication, tool execution or rejected-local fallback. "
                    + "Existing saved drafts and unsaved Sessions composer buffers are unchanged. Cancel writes no conversation message.\n\n"
                    + prepared.Expanded.UserRequest;
                opened.ConfirmSubmit.IsEnabled = true;
                opened.CancelReview.IsEnabled = true;
                opened.Status.Text = "Review the whole exact expanded USER text and immutable target above; separate native confirmation is required.";
            }
            else if (string.Equals(action, "ConfirmSubmit", StringComparison.Ordinal))
            {
                var prepared = reviewed ?? throw new InvalidOperationException("Review the current expanded request and exact target first.");
                var selection = Interlocked.Read(ref targetRevision);
                bool Current() => Eligible(captured) && ReferenceEquals(Volatile.Read(ref reviewed), prepared)
                    && Volatile.Read(ref target) == prepared.Target && Interlocked.Read(ref targetRevision) == selection
                    && captured.IsUseCurrent(prepared.Expanded);
                var receipt = await submission.Submit(prepared, main.IsPromptSourceCurrent, Current, token);
                // A known COMMIT/terminal receipt stays truthful even when cancellation or closure follows.
                if (!ReferenceEquals(window, opened)) { return; }
                submissionHeld = true;
                Volatile.Write(ref reviewed, null);
                captured.Dispose();
                foreach (var input in opened.Inputs.Values) { input.Text = string.Empty; input.IsEnabled = false; }
                opened.RetirePreview();
                opened.Preview.IsEnabled = false;
                opened.RefreshTargets.IsEnabled = false;
                opened.NextTargets.IsEnabled = false;
                opened.Targets.IsEnabled = false;
                opened.Status.Text = $"Message retained; model generation unavailable. HOST {receipt.Message.Outcome} / {receipt.Message.Reason}. "
                    + $"Session {receipt.Message.Request.SessionId.Value:D}; message sequence {receipt.Message.Sequence}; "
                    + $"request {receipt.Message.Request.RequestId.Value:D}; task {receipt.Task.Request.TaskId.Value:D}. "
                    + "Inspect this exact session through Sessions > Read conversation and task receipts. No assistant answer; no replay.";
            }
        }
        catch (ConversationSubmissionUnconfirmedException exception)
        {
            submissionHeld = true;
            RetireReview();
            if (ReferenceEquals(window, opened))
            {
                opened.Preview.IsEnabled = false;
                opened.Use.IsEnabled = false;
                opened.Status.Text = $"Submission unconfirmed; content may already be retained. NEVER automatically retry or infer rollback. "
                    + $"Inspect exact session {exception.Request.SessionId.Value:D}, request {exception.Request.RequestId.Value:D}, "
                    + $"task {exception.Request.TaskId.Value:D} through conversation/task receipts.";
            }
        }
        catch (OperationCanceledException) { if (ReferenceEquals(window, opened)) { opened.RetirePreview(); } }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            PreparationFailed(logger, exception.GetType().Name);
            if (ReferenceEquals(window, opened))
            {
                opened.RetirePreview();
                opened.Status.Text = exception is InvalidDataException or InvalidOperationException
                    ? exception.Message : "Prompt preparation unavailable. Cancel and reselect the exact reviewed revision.";
            }
        }
        finally { busy = false; EnableReview(); }
    }

    private void OnPrivacyClosed(object? sender, EventArgs args) => DispatchClose();
    private void OnInputChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.CommandText) or nameof(MainViewModel.CallPolicyRevision))
        { DispatchClose(); }
    }
    private void OnCatalogueChanged(object? sender, EventArgs args) => DispatchClose();
    private void OnReloadFailed(object? sender, ArtifactCatalogueReloadFailedEventArgs args) => DispatchClose();

    private void DispatchClose()
    {
        var opened = window;
        if (Dispatcher.UIThread.CheckAccess()) { Close(); }
        else { Dispatcher.UIThread.Post(() => { if (ReferenceEquals(window, opened)) { Close(); } }); }
    }

    private void Close()
    {
        var opened = window;
        Clear();
        opened?.Close();
    }

    internal async Task CloseAndWait()
    {
        closing = true;
        try
        {
            if (Dispatcher.UIThread.CheckAccess()) { Close(); }
            else { await Dispatcher.UIThread.InvokeAsync(Close); }
#pragma warning disable VSTHRD003 // Join only this controller's cancelled callback; asynchronous awaiting leaves its Avalonia dispatcher free.
            await activeOperation;
#pragma warning restore VSTHRD003
        }
        finally { activeOperation = Task.CompletedTask; closing = false; }
    }

    private void Clear()
    {
        var cancellation = lifetime;
        lifetime = null;
        draft?.Dispose();
        draft = null;
        Volatile.Write(ref target, null);
        nextTarget = null;
        Interlocked.Increment(ref targetRevision);
        Interlocked.Increment(ref reviewRevision);
        Volatile.Write(ref reviewed, null);
        submissionHeld = false;
        var opened = window;
        window = null;
        opened?.ClearPrivateText();
        try { cancellation?.Cancel(); }
        finally { cancellation?.Dispose(); }
    }

    public void Dispose()
    {
        disposed = true;
        main.PromptPreparationRequested -= Open;
        main.PromptPreparationSuperseded -= OnPrivacyClosed;
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        main.PropertyChanged -= OnInputChanged;
        catalogue.Changed -= OnCatalogueChanged;
        catalogue.ReloadFailed -= OnReloadFailed;
        main.BindPromptPreparationShutdown(null);
        Close();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await CloseAndWait();
    }
}
