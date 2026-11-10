using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Kora.Application.Definitions;

namespace Kora;

internal sealed class OwnedInstructionRebaseReviewWindow : Window
{
    private readonly OwnedInstructionRebaseReviewView view;
    internal OwnedInstructionRebaseReviewWindow(OwnedDraftRebase proposal, Func<bool> eligible)
    {
        Title = "Review source-preserving Rebase";
        Width = 1100; Height = 740; MinWidth = 700; MinHeight = 500;
        view = new(proposal, eligible);
        Content = view;
        view.DecisionRequested += accepted =>
        {
            if (accepted) { Close(true); }
            else { Hide(); Close(); }
        };
        Opened += (_, _) => view.Start();
        Closed += (_, _) => Retire();
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (!ReferenceEquals(args.Source, this)) { return; }
            var forwarded = new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = args.Key, KeyModifiers = args.KeyModifiers };
            view.RaiseEvent(forwarded);
            args.Handled = forwarded.Handled;
        }, RoutingStrategies.Tunnel);
    }
#pragma warning disable VSTHRD003 // Compatibility adapter exposes its exact queued withdrawal receipt.
    internal Task Withdrawal => view.Withdrawal;
#pragma warning restore VSTHRD003
    internal void Withdraw() => view.Withdraw();
    internal void Retire() { view.Retire(); Content = null; }
}
