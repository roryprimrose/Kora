using Avalonia.Controls;

using Kora.Application.Definitions;

namespace Kora;

internal sealed partial class OwnedInstructionEditorWindow
{
    internal void CancelRebaseReview()
    {
        if (activeReview is OwnedInstructionRebaseReviewWindow review) { review.Withdraw(); }
    }

    internal async Task<bool> ReviewRebase(OwnedDraftRebase proposal, Func<bool> current, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ReviewRebase(proposal, current, token)); }
        if (!IsVisible || !current()) { throw new InvalidOperationException("The exact Rebase review lifetime expired."); }
        var dialog = new OwnedInstructionRebaseReviewWindow(proposal, () => IsVisible && current());
        activeReview = dialog;
        var reply = dialog.ShowDialog<bool?>(this);
        if (!IsVisible || !current())
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
