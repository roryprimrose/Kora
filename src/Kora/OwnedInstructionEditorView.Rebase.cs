using Avalonia.Controls;

using Kora.Application.Definitions;

namespace Kora;

internal sealed partial class OwnedInstructionEditorView
{
    private OwnedInstructionRebaseReviewView? rebaseReview;
    internal void CancelRebaseReview()
    {
        rebaseReview?.Withdraw();
        if (activeReview is OwnedInstructionRebaseReviewWindow review) { review.Withdraw(); }
    }

    internal async Task<bool> ReviewRebase(OwnedDraftRebase proposal, Func<bool> current, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ReviewRebase(proposal, current, token)); }
        if (!IsNativePresented || !current()) { throw new InvalidOperationException("The exact Rebase review lifetime expired."); }
        var native = CapturePresentation();
        if (Owner is ResponseWindow primary)
        {
            var body = new OwnedInstructionRebaseReviewView(proposal, () => native() && current());
            rebaseReview = body;
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            body.DecisionRequested += accepted => decision.TrySetResult(accepted && native() && current());
            using var inlineRegistration = token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(body.Withdraw));
            try
            {
                primary.ShowNativeReview(body, "Review source-preserving Rebase", body, () => native() && current(),
                    shown => { if (!shown) { body.Withdraw(); } });
                body.Start();
                return await decision.Task && native() && current() && !token.IsCancellationRequested;
            }
            finally
            {
                if (ReferenceEquals(rebaseReview, body)) { rebaseReview = null; }
                body.Retire();
                primary.RemoveNativeReview(body);
            }
        }
        var dialog = new OwnedInstructionRebaseReviewWindow(proposal, () => native() && current());
        activeReview = dialog;
        var reply = dialog.ShowDialog<bool?>(Owner);
        if (!native() || !current())
        {
            dialog.Withdraw();
            await dialog.Withdrawal;
        }
        using var registration = token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(activeReview, dialog) && dialog.IsVisible) { dialog.Withdraw(); }
        }));
        try { return await reply == true && current() && !token.IsCancellationRequested; }
        finally
        {
            dialog.Retire();
            if (ReferenceEquals(activeReview, dialog)) { activeReview = null; }
        }
    }
}
