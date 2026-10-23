using Avalonia.Controls;
using Avalonia.Input;

namespace Kora;

internal sealed partial class CompactSessionWindow : Window
{
    internal event EventHandler? SessionsRequested;
    internal event EventHandler? NewRequestRequested;
    internal Task ConversationOperation { get; private set; } = Task.CompletedTask;
    internal Task ConversationCancellationOperation { get; private set; } = Task.CompletedTask;

    internal CompactSessionWindow(SessionsViewModel model, SessionsPresentationOwner owner)
    {
        InitializeComponent();
        DataContext = model;
        _ = new NativeDraftWrapping(this, ConversationComposer, WrapDraftText, model, model.CaptureDraftPresentationEligibility);
        owner.Attach(this);
        Sessions.Click += (_, _) => SessionsRequested?.Invoke(this, EventArgs.Empty);
        NewRequest.Click += (_, _) => NewRequestRequested?.Invoke(this, EventArgs.Empty);
        ReadConversation.Click += async (_, _) => await (ConversationOperation = model.ReadConversationAsync());
        ReadSelectedAnswer.Click += async (_, _) => await (ConversationOperation = model.ReadSelectedAnswerAsync());
        CancelAnswerRead.Click += (_, _) => model.CancelAnswerRead();
        Closed += (_, _) => model.CancelAnswerRead();
        NextConversation.Click += async (_, _) => await (ConversationOperation = model.ReadConversationAsync(next: true));
        SearchConversation.Click += async (_, _) => await (ConversationOperation = model.SearchConversationAsync());
        NextConversationSearch.Click += async (_, _) => await (ConversationOperation = model.SearchConversationAsync(next: true));
        CancelConversationSearch.Click += (_, _) => model.ClearConversationSearch();
        ClearConversationSearch.Click += (_, _) => model.ClearConversationSearch();
        foreach (var field in new[] { ConversationQuery, ConversationSubmittedFrom, ConversationSubmittedThrough })
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
        RecoverConversationDraft.Click += async (_, _) => await (ConversationOperation = model.RecoverLegacyUnsentAsync());
        DiscardComposer.Click += async (_, _) => await (ConversationOperation = model.DiscardComposerBufferAsync());
        SubmitConversation.Click += async (_, _) => await (ConversationOperation = model.SubmitConversationAsync());
        SubmitConversationWithClarification.Click += async (_, _) =>
            await (ConversationOperation = model.SubmitConversationWithClarificationAsync());
        RefreshRunningConversation.Click += async (_, _) => await model.RefreshRunningConversationAsync();
        CancelRunningConversation.Click += async (_, _) =>
            await (ConversationCancellationOperation = model.CancelRunningConversationAsync());
        CloseView.Click += (_, _) => Close();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && !args.Handled) { Close(); args.Handled = true; }
        };
    }
}
