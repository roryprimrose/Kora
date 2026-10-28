using Avalonia.Controls;
using Avalonia.Media;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;

using AllWorkStopOutcome = Kora.Application.Hosting.AllWorkStopResult.OutcomeKind;

namespace Kora;

internal sealed class ApplicationLifecycleController : IDisposable
{
    private readonly MainViewModel main;
    private readonly SessionConversationService conversations;
    private readonly SessionQueueService queue;
    private readonly NativeQuestionHost questions;
    private readonly HostTaskCoordinator tasks;
    private readonly NativeDetailRenderer renderer;
    private readonly Func<Window> owner;
    private Window? reviewWindow;
    private QuestionWindow? questionWindow;
    private NativeQuestionViewModel? question;
    private CancellationTokenSource? activeReview;
    private bool disposed;

    internal ApplicationLifecycleController(MainViewModel main, SessionConversationService conversations,
        SessionQueueService queue, NativeQuestionHost questions, HostTaskCoordinator tasks,
        NativeDetailRenderer renderer, Func<Window> owner)
    {
        this.main = main;
        this.conversations = conversations;
        this.queue = queue;
        this.questions = questions;
        this.tasks = tasks;
        this.renderer = renderer;
        this.owner = owner;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.BindSubmittedWorkShutdown(PrepareAsync);
        main.BindAllWorkStop(StopAllAsync);
    }

    private async Task<ApplicationLifecycleShutdown?> PrepareAsync(ApplicationLifecycleOperation operation,
        RequestOrigin origin, Func<bool> admitted)
    {
        var prepared = await PrepareCoreAsync(operation, origin, admitted, CancellationToken.None);
        return prepared is null ? null : new(() => prepared.Current(),
            async () =>
            {
                await conversations.ConfirmApplicationLifecycleAsync(prepared.Review, CancellationToken.None);
                await queue.ConfirmApplicationLifecycleAsync(prepared.Finite, CancellationToken.None);
            }, prepared.Dispose);
    }

    private sealed record Prepared(ApplicationLifecycleReview Review, SessionQueueLifecycleReview Finite,
        Func<bool> Current) : IDisposable
    {
        public void Dispose() { Review.Dispose(); Finite.Dispose(); }
    }

    private async Task<AllWorkStopResult> StopAllAsync(RequestOrigin origin, Func<bool> admitted, CancellationToken token)
    {
        Prepared? prepared = null;
        try
        {
            prepared = await PrepareCoreAsync(ApplicationLifecycleOperation.StopAllWork, origin, admitted, token);
            if (prepared is null) { return new(admitted() ? AllWorkStopOutcome.Cancelled : AllWorkStopOutcome.Stale); }
            main.PresentAllWorkJoining();
            // Both owners retain their own accepted resources even when the other owner fails.
            await Task.WhenAll(conversations.ConfirmApplicationLifecycleAsync(prepared.Review, CancellationToken.None),
                queue.ConfirmApplicationLifecycleAsync(prepared.Finite, CancellationToken.None));
            var empty = prepared.Review.Work.Length == 0 && prepared.Finite.Queues.Length == 0;
            return Result(empty ? AllWorkStopOutcome.NothingToStop : AllWorkStopOutcome.Completed);
        }
        catch (OperationCanceledException)
        { return Result(prepared is { Review.StopAccepted: true } or { Finite.StopAccepted: true }
            ? AllWorkStopOutcome.PartialUnconfirmed : AllWorkStopOutcome.Cancelled); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            main.ReportAllWorkStopFailure(exception);
            return Result(prepared is { Review.StopAccepted: true } or { Finite.StopAccepted: true }
                ? AllWorkStopOutcome.PartialUnconfirmed : AllWorkStopOutcome.Stale);
        }
        finally { prepared?.Dispose(); }

        AllWorkStopResult Result(AllWorkStopOutcome outcome) => new(outcome,
            prepared?.Review.Work ?? [], prepared?.Review.Results ?? [],
            prepared?.Finite.TerminalQueues ?? [], prepared?.Finite.Joined ?? false, prepared?.Finite.Queues ?? []);
    }

    private async Task<Prepared?> PrepareCoreAsync(ApplicationLifecycleOperation operation,
        RequestOrigin origin, Func<bool> admitted, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (activeReview is not null) { throw new InvalidOperationException("Another submitted-work review is active."); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(1));
        activeReview = deadline;
        ApplicationLifecycleReview? review = null;
        SessionQueueLifecycleReview? finite = null;
        var transferred = false;
        bool Current() => !disposed && admitted();
        try
        {
            finite = await queue.ReviewApplicationLifecycleAsync(origin, Current, deadline.Token);
            review = await conversations.ReviewApplicationLifecycleAsync(operation, origin,
                () => Current() && queue.IsApplicationLifecycleCurrent(finite), deadline.Token);
            bool ReviewCurrent() => Current() && conversations.IsApplicationLifecycleCurrent(review)
                && queue.IsApplicationLifecycleCurrent(finite);
            var finiteWork = finite.Queues.SelectMany(item => item.Entries)
                .Where(item => item.State is SessionQueueState.Pending or SessionQueueState.Running).ToArray();
            if (review.Work.Length != 0 || finiteWork.Length != 0)
            {
                var content = new StackPanel { Margin = new(18), Spacing = 12 };
                content.Children.Add(new TextBlock
                {
                    Text = $"{operation}: all affected submitted work",
                    FontSize = 22,
                    TextWrapping = TextWrapping.Wrap,
                });
                content.Children.Add(new TextBlock
                {
                    Text = "These are already submitted requests, separate from unsent text and editor changes. "
                        + "Cancel leaves requests unchanged. Confirm withdraws pending requests and stops running originals, "
                        + "then waits for their real resources. Retained history and grants are not deleted. "
                        + "Already dispatched compatibility reads finish under their existing rules.",
                    TextWrapping = TextWrapping.Wrap,
                });
                foreach (var work in review.Work)
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = $"{work.Session.Metadata?.Name.Value ?? "Unnamed conversation"}: "
                            + $"{work.Execution?.ToString() ?? work.Pending?.ToString()} ({work.Request.Origin})\n"
                            + work.Description,
                        TextWrapping = TextWrapping.Wrap,
                    });
                }
                foreach (var work in finiteWork)
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = $"Compatibility {work.ReadProfile} read: {work.State} ({work.Request.Origin})",
                        TextWrapping = TextWrapping.Wrap,
                    });
                }
                var opened = reviewWindow = new Window
                {
                    Title = $"{operation}: review all submitted requests",
                    Width = 740, Height = 560,
                    Content = new ScrollViewer { Content = content },
                };
                opened.Closed += (_, _) =>
                {
                    opened.Content = null;
                    if (!transferred) { deadline.Cancel(); }
                };
                opened.Show(owner());
                var subject = review.Work.Length != 0 ? review.Work[0].Request.SessionId : finiteWork[0].Request.SessionId;
                var request = new HostRequest(new(Guid.NewGuid()), subject, new(Guid.NewGuid()), origin);
                var confirmed = await questions.AskApplicationLifecycleAsync(request, operation,
                    $"{operation}: separately confirm stopping all {review.Work.Length} submitted conversation requests "
                    + $"and retiring {finiteWork.Length} compatibility reads shown in the affected-work review. "
                    + $"The confirmation must use the original {origin} channel. Cancel makes no stop request.",
                    review.ExpiresAt, ReviewCurrent, tasks, state =>
                    {
                        question = state;
                        questionWindow = new(state, renderer);
                        questionWindow.Show(opened);
                        return Task.CompletedTask;
                    }, deadline.Token);
                if (!confirmed || !ReviewCurrent()) { return null; }
            }
            transferred = true;
            var acceptedReview = review;
            var acceptedFinite = finite;
            return new(acceptedReview, acceptedFinite, () => Current() && conversations.IsApplicationLifecycleCurrent(acceptedReview)
                    && queue.IsApplicationLifecycleCurrent(acceptedFinite));
        }
        finally
        {
            activeReview = null;
            question?.Close("Voluntary lifecycle decision ended.");
            questionWindow?.Close();
            reviewWindow?.Close();
            questionWindow = null;
            reviewWindow = null;
            question = null;
            if (!transferred) { review?.Dispose(); finite?.Dispose(); }
        }
    }

    private void OnPrivacyClosed(object? sender, EventArgs args)
    {
        activeReview?.Cancel();
        question?.Close("Private presentation retired. No lifecycle confirmation remains.");
        questionWindow?.Close();
        reviewWindow?.Close();
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        main.BindSubmittedWorkShutdown(null);
        main.BindAllWorkStop(null);
        OnPrivacyClosed(this, EventArgs.Empty);
    }
}
