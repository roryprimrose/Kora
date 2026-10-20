using Avalonia.Controls;

using Kora.Application;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class QuestionWindowController : IDisposable
{
    private readonly MainViewModel main;
    private readonly NativeQuestionHost host;
    private readonly DurableVersionQuery query;
    private readonly IApplicationInfo info;
    private readonly NativeDetailRenderer renderer;
    private readonly ILogger<QuestionWindowController> logger;
    private QuestionWindow? window;
    private NativeQuestionViewModel? state;
    private bool running;
    private bool disposed;
    private int ownerUsers;
    internal bool IsOwnerInUse => running || ownerUsers > 0 || window is not null;

    internal QuestionWindowController(MainViewModel main, NativeQuestionHost host, DurableVersionQuery query,
        IApplicationInfo info, NativeDetailRenderer renderer, Func<bool> ownsDesktop,
        ILogger<QuestionWindowController> logger, Func<long>? admissionRevision = null)
    {
        this.main = main;
        this.host = host;
        this.query = query;
        this.info = info;
        this.renderer = renderer;
        this.logger = logger;
        host.BindGate(() => !disposed && ownsDesktop() && main.CanRevealPrivatePresentation);
        if (admissionRevision is not null) { host.BindAdmissionRevision(admissionRevision); }
        host.BindCallObservation(() => main.CallObservation);
        main.BindResponseReadDecisionGate(() => !disposed && !IsOwnerInUse);
        main.PrivacyClosureRequested += OnPrivacyClosure;
    }

    internal async Task ReviewVersionAsync(Window owner)
    {
        if (disposed) { return; }
        if (!main.CanRevealPrivatePresentation)
        {
            main.ReportHostInteractionFailure("Native question denied: privacy or host lifecycle admission is unavailable. Return through the tray after resolving the blocker; no query was dispatched.");
            return;
        }
        if (running) { await ReturnToCurrentQuestionAsync(owner); return; }
        window?.Close();
        main.RetireCurrentResponseSpeech();
        running = true;
        try
        {
            var version = string.Empty;
            var receipt = await query.RunAsync(RequestOrigin.LocalUi, () =>
            {
                if (!main.CanRevealPrivatePresentation) { throw new InvalidOperationException("Privacy closed before the local query."); }
                version = info.Version;
                return Task.CompletedTask;
            }, CancellationToken.None, async _ =>
                await host.AskVersionAsync(model =>
                {
                    OpenExact(owner, model);
                    return Task.CompletedTask;
                }, CancellationToken.None));
            if (!disposed && main.CanRevealPrivatePresentation)
            {
                window?.ReportOutcome(receipt.State == HostTaskState.Cancelled
                    ? "Exact local-version task and question cancelled before dispatch. Durable terminal/audit committed; no effect ran."
                    : $"Local version: {version}\n{DurableVersionQuery.StorageDisclosure}"
                    + $"\nVerified durable receipt: {receipt.State}. No model, grant or external effect was used."
                    + " Optional explicit question voice input is local and bounded.");
            }
        }
        catch (OperationCanceledException)
        {
            const string ended = "Question wait ended or was cancelled. No version returned or successful receipt claimed. Inspect durable Interrupted/Unknown status; no old work is replayed.";
            if (!disposed)
            {
                if (window is not null) { window.ReportOutcome(ended); }
                else { main.ReportHostInteractionFailure(ended); }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            const string failure = "Native version review failed. No successful query receipt or approval is claimed. Close and start a fresh review; uncertain work is not replayed.";
            if (!disposed)
            {
                if (window is not null) { window.ReportOutcome(failure); }
                else { main.ReportHostInteractionFailure(failure); }
            }
            QueryFailure(logger, exception.GetType().FullName);
        }
        finally
        {
            running = false;
            if (window is null) { state = null; }
        }
    }

    internal async Task ReturnToCurrentQuestionAsync(Window owner)
    {
        if (disposed) { return; }
        ownerUsers++;
        try
        {
            var model = await host.ReturnToCurrentQuestionAsync(CancellationToken.None);
            if (disposed) { return; }
            if (model is null)
            {
                main.ReportHostInteractionFailure("Current question recovery unavailable. Only the still-live original current-run local-version wait can reopen. Inspect retained status and start a fresh review; no old task is replayed.");
                return;
            }
            OpenExact(owner, model);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!disposed)
            {
                main.ReportHostInteractionFailure("Current question recovery failed. Storage/audit or host admission is unavailable; no answer, dispatch or successful recovery is claimed. Inspect status before a fresh request.");
            }
            QueryFailure(logger, exception.GetType().FullName);
        }
        finally
        {
            ownerUsers--;
        }
    }

    private void OpenExact(Window owner, NativeQuestionViewModel model)
    {
        model.RefreshEligibility();
        if (disposed || !model.IsEditable) { return; }
        if (window is not null)
        {
            if (ReferenceEquals(state, model)) { window.Activate(); }
            return;
        }
        state = model;
        window = new(model, renderer);
        var opened = window;
        opened.Closed += (_, _) =>
        {
            if (ReferenceEquals(window, opened))
            {
                window = null;
                if (!running) { state = null; }
            }
        };
        window.Show(owner);
        window.Activate();
    }

    private void OnPrivacyClosure(object? sender, EventArgs args)
    {
        state?.Close("Privacy closed. The original question cannot be answered through this window.");
        window?.Close();
        window = null;
        state = null;
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        host.BindGate(static () => false);
        OnPrivacyClosure(this, EventArgs.Empty);
        host.Dispose();
    }
}
