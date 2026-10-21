using Avalonia.Controls;
using Avalonia.Input;

namespace Kora;

internal sealed partial class CompactSessionWindow : Window
{
    internal event EventHandler? SessionsRequested;
    internal event EventHandler? NewRequestRequested;
    internal Task ConversationOperation { get; private set; } = Task.CompletedTask;

    internal CompactSessionWindow(SessionsViewModel model, SessionsPresentationOwner owner)
    {
        InitializeComponent();
        DataContext = model;
        _ = new NativeDraftWrapping(this, ConversationComposer, WrapDraftText, model, model.CaptureDraftPresentationEligibility);
        owner.Attach(this);
        Sessions.Click += (_, _) => SessionsRequested?.Invoke(this, EventArgs.Empty);
        NewRequest.Click += (_, _) => NewRequestRequested?.Invoke(this, EventArgs.Empty);
        ReadConversation.Click += async (_, _) => await (ConversationOperation = model.ReadConversationAsync());
        NextConversation.Click += async (_, _) => await (ConversationOperation = model.ReadConversationAsync(next: true));
        SearchConversation.Click += async (_, _) => await (ConversationOperation = model.SearchConversationAsync());
        NextConversationSearch.Click += async (_, _) => await (ConversationOperation = model.SearchConversationAsync(next: true));
        CancelConversationSearch.Click += (_, _) => model.ClearConversationSearch();
        ClearConversationSearch.Click += (_, _) => model.ClearConversationSearch();
        foreach (var field in new[] { ConversationQuery })
        {
            field.KeyDown += async (_, args) =>
            {
                if (args.Key == Key.Enter && model.CanSearchConversation)
                {
                    args.Handled = true;
                    await (ConversationOperation = model.SearchConversationAsync());
                }
                else if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    model.ClearConversationSearch();
                }
            };
        }
        RefreshWork.Click += async (_, _) => await (ConversationOperation = model.RefreshWorkAsync());
        SaveConversationDraft.Click += async (_, _) => await (ConversationOperation = model.SaveConversationDraftAsync());
        RecoverConversationDraft.Click += async (_, _) => await (ConversationOperation = ConversationDraftRecoveryWindow.RecoverAsync(this, model));
        Closed += (_, _) => model.CancelConversationDraftRecovery();
        ReloadConversationDraft.Click += async (_, _) => await (ConversationOperation = model.ReloadConversationDraftAsync());
        DiscardComposer.Click += (_, _) => model.DiscardComposerBuffer();
        ReviewClearConversationDraft.Click += (_, _) => model.ReviewClearConversationDraft();
        ClearConversationDraft.Click += async (_, _) => await (ConversationOperation = model.ClearSavedConversationDraftAsync());
        SubmitConversation.Click += async (_, _) => await (ConversationOperation = model.SubmitConversationAsync());
        CloseView.Click += (_, _) => Close();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && !args.Handled) { Close(); args.Handled = true; }
        };
    }
}
