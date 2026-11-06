using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        BindDecision(Mark, reviewed: true);
        BindDecision(Unmark, reviewed: false);
        AttachedToVisualTree += (_, _) => presentation = new(this);
        DetachedFromVisualTree += (_, _) => { presentation?.Dispose(); presentation = null; };
    }

    private SessionsViewModel Model => DataContext as SessionsViewModel
        ?? throw new InvalidOperationException("The exact conversation review owner is unavailable.");

    private void BindDecision(Button button, bool reviewed)
    {
        Func<Task>? captured = null;
        button.AddHandler(InputElement.PointerPressedEvent,
            (_, _) => captured = Model.CaptureAnswerReviewDecision(reviewed), RoutingStrategies.Tunnel);
        button.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key is Key.Space or Key.Enter) { captured = Model.CaptureAnswerReviewDecision(reviewed); }
        }, RoutingStrategies.Tunnel);
        button.Click += async (_, _) =>
        {
            var decision = captured ?? Model.CaptureAnswerReviewDecision(reviewed);
            captured = null;
            await (Operation = decision());
        };
    }

    private Task Read(ConversationAnswerReviewFilter selectedFilter)
    {
        filter = selectedFilter;
        return Model.ReadAnswerReviewsAsync(filter);
    }
}
