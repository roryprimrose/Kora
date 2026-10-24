using System.ComponentModel;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class InstructionUseController(MainViewModel main, InstructionUseWorkflow workflow,
    Func<bool> ownsHost, Func<long> ownerRevision, ILogger<InstructionUseController> logger) : IDisposable, IAsyncDisposable
{
    private InstructionUseWindow? window;
    private InstructionUseReview? review;
    private Func<bool>? originalEligibility;
    private long capturedOwner;
    private long capturedCall;
    private bool disposed;
    private bool presenting;
    private Task active = Task.CompletedTask;
#pragma warning disable VSTHRD003 // Observes the route this controller published before starting its owned work.
    internal Task ActiveOperation => active;
#pragma warning restore VSTHRD003

    internal void Bind()
    {
        main.InstructionUseRequested += Route;
        main.PrivacyClosureRequested += PrivacyClosed;
        main.PropertyChanged += MainChanged;
        main.BindInstructionUseShutdown(Shutdown);
    }

    private bool Current() => !disposed && ownsHost() && ownerRevision() == capturedOwner
        && main.CallPolicyRevision == capturedCall && !main.IsProtectedCall
        && main.CanRevealPrivatePresentation && main.IsSkillReviewCallEligible
        && !main.IsResponseInteractionPending && originalEligibility?.Invoke() == true;

    private async Task<Guid?> Route(InstructionUseCommand command, RequestOrigin origin, Func<bool> eligible, Guid? expected)
    {
        if (command.Action is "Cancel" or "Stop")
        {
            await CloseAndWait();
            return null;
        }
        if (!active.IsCompleted)
        {
            main.ReportHostInteractionFailure("Instruction work is still owned. Stop and join it before another operation.");
            return null;
        }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        active = completion.Task;
        try
        {
            if (!eligible()) { throw new InvalidOperationException("Original command admission expired."); }
            if (command.Action is "Review")
            {
                if (presenting) { throw new InvalidOperationException("Cancel the current instruction review before replacing it."); }
                capturedOwner = ownerRevision();
                capturedCall = main.CallPolicyRevision;
                originalEligibility = eligible;
                presenting = true;
                review = await workflow.ReviewAsync(command.Source!, command.Target!, command.Body!, origin, Current, CancellationToken.None);
                if (!Current()) { throw new OperationCanceledException("Review presentation was revoked."); }
#pragma warning disable MA0147 // Native button callback is a handled async-void boundary.
                var opened = new InstructionUseWindow(Start);
#pragma warning restore MA0147
                window = opened;
                opened.Review.Text = review.Text;
                opened.Confirm.IsEnabled = review.CanConfirm;
                opened.Status.Text = "No model call or message retention yet. Separately confirm, cancel or close.";
                opened.Closed += WindowClosed;
                opened.Show();
                return review.Id;
            }
            if (command.Action is not "Confirm" || review is null || expected != review.Id || window is not { } target || !Current())
            { throw new InvalidOperationException("Only the exact current pending review can be confirmed."); }
            var exact = review;
            review = null;
            main.RetireInstructionUseReview(exact.Id);
            target.Confirm.IsEnabled = false;
            target.Status.Text = "Submitting the original one-shot local request. Stop joins owned work; it does not claim rollback.";
            var receipt = await workflow.ConfirmAsync(exact.Id, () => Current() && eligible(), CancellationToken.None);
            if (!Current() || !ReferenceEquals(window, target))
            { throw new OperationCanceledException("Private instruction result presentation was revoked."); }
            target.Status.Text = "Retained original USER request. HOST " + receipt.Message.Outcome + " / "
                + receipt.Message.Reason + " | task " + receipt.Task.State
                + ". No instruction applies to the next request. No automatic retry.";
            target.Result.Text = receipt.Message.AssistantResponse is { } answer
                ? "Genuine local ASSISTANT answer\n" + answer.Body : "No assistant answer retained.";
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            await Close();
            Failed(logger, exception.GetType().Name);
            main.ReportHostInteractionFailure("Instruction request unavailable, revoked or unconfirmed. "
                + "Inspect the original conversation for any retained USER/task terminal; no substitute answer or automatic retry.");
            return null;
        }
        finally { completion.SetResult(); }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native button boundary; Route catches and reports operation failures.")]
    private async void Start(string action)
    {
        var expected = review?.Id;
        var eligible = main.CaptureInstructionEditorEligibility();
        await HostRequestRunner.RunAsync(RequestOrigin.LocalUi,
            () => Route(new(action), RequestOrigin.LocalUi, eligible, expected), HostActivityLayer.Desktop);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native lifecycle event; closure failures are caught and reported.")]
    private async void WindowClosed(object? sender, EventArgs args) => await CloseReported();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Privacy event retains and joins the exact workflow; failures are reported.")]
    private async void PrivacyClosed(object? sender, EventArgs args) => await CloseReported();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Property event retains and joins revoked work; failures are reported.")]
    private async void MainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (presenting && !Current()) { await CloseReported(); }
    }

    private async Task CloseReported()
    {
        try { await CloseAndWait(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception.GetType().Name);
            main.ReportHostInteractionFailure("Instruction work release is unconfirmed. No successful cancellation or rollback claimed.");
        }
    }

    private async Task CloseAndWait()
    {
        var operation = active;
        await Close();
#pragma warning disable VSTHRD003 // Join the exact route captured before closure; it includes terminal reporting.
        await operation;
#pragma warning restore VSTHRD003
    }

    private async Task Close()
    {
        presenting = false;
        originalEligibility = null;
        main.RetireInstructionUseReview(review?.Id);
        review = null;
        var opened = window;
        window = null;
        if (opened is not null)
        {
            opened.Closed -= WindowClosed;
            opened.Clear();
            opened.Close();
        }
        await workflow.StopAsync();
    }

    private async Task Shutdown()
    {
        await CloseAndWait();
        await workflow.DisposeAsync();
        Unbind();
    }

    public ValueTask DisposeAsync() => new(Shutdown());

    public void Dispose() => Unbind();

    private void Unbind()
    {
        disposed = true;
        main.InstructionUseRequested -= Route;
        main.PrivacyClosureRequested -= PrivacyClosed;
        main.PropertyChanged -= MainChanged;
        main.BindInstructionUseShutdown(null);
    }
}
