using Avalonia.Controls;
using Avalonia.Interactivity;

using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsPresentationOwner
{
    internal Task ModelMemoryOperation { get; private set; } = Task.CompletedTask;

    private void OnModelMemoryProposal(object? sender, RoutedEventArgs args)
    {
        if (revoked || args.Source is not Button button) { return; }
        if (button is { Name: "ReviewModelMemoryProposal", DataContext: ConversationMessage message }
            && model.CanReviewModelMemoryProposal)
        {
            args.Handled = true;
            ModelMemoryOperation = model.ReviewModelMemoryProposalAsync(message);
        }
        else if (string.Equals(button.Name, "AcceptModelMemoryProposal", StringComparison.Ordinal) && model.CanAcceptModelMemoryProposal)
        {
            args.Handled = true;
            ModelMemoryOperation = model.AcceptModelMemoryProposalAsync();
        }
        else if (string.Equals(button.Name, "RejectModelMemoryProposal", StringComparison.Ordinal) && model.CanAcceptModelMemoryProposal)
        {
            args.Handled = true;
            ModelMemoryOperation = model.RejectModelMemoryProposalAsync();
        }
        else if (string.Equals(button.Name, "AdmitModelMemoryProposal", StringComparison.Ordinal) && model.CanAdmitModelMemoryProposal)
        {
            args.Handled = true;
            ModelMemoryOperation = model.AdmitModelMemoryProposalAsync();
        }
        else if (string.Equals(button.Name, "CancelModelMemoryProposal", StringComparison.Ordinal))
        {
            args.Handled = true;
            model.CancelModelMemoryProposal();
        }
    }
}
