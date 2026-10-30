using Avalonia.Controls;
using Avalonia.VisualTree;

using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class AnswerReviewPanel : UserControl
{
    private ConversationAnswerReviewFilter filter;
    private ReplyPresentationFence? presentation;
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public AnswerReviewPanel()
    {
        InitializeComponent();
        ReadNotMarked.Click += async (_, _) => await (Operation = Read(ConversationAnswerReviewFilter.NotMarkedReviewed));
        ReadAll.Click += async (_, _) => await (Operation = Read(ConversationAnswerReviewFilter.All));
        Next.Click += async (_, _) => await (Operation = Model.ReadAnswerReviewsAsync(filter, next: true));
        OpenAnswer.Click += async (_, _) =>
        {
            var live = presentation is not null && IsEffectivelyVisible
                ? presentation.Capture() : static () => false;
            await (Operation = Model.OpenAnswerReviewAsync(live));
        };
        Mark.Click += async (_, _) => await (Operation = Model.SetAnswerReviewedAsync(reviewed: true));
        Unmark.Click += async (_, _) => await (Operation = Model.SetAnswerReviewedAsync(reviewed: false));
        AttachedToVisualTree += (_, _) => presentation = new(this);
        DetachedFromVisualTree += (_, _) => { presentation?.Dispose(); presentation = null; };
    }

    private SessionsViewModel Model => DataContext as SessionsViewModel
        ?? throw new InvalidOperationException("The exact conversation review owner is unavailable.");

    private Task Read(ConversationAnswerReviewFilter selectedFilter)
    {
        filter = selectedFilter;
        return Model.ReadAnswerReviewsAsync(filter);
    }
}
