using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private ConversationAnswerReviewPage? answerReviewPage;
    private IReadOnlyList<ConversationAnswerReviewRow> answerReviewRows = [];
    private ConversationAnswerReviewRow? selectedAnswerReview;
    private ConversationAnswerReviewRow? openedAnswerReview;
    private Func<bool>? answerReviewPresentation;
    private string answerReviewStatus = "Read answers not marked reviewed in this exact conversation. Opening or rendering never marks an answer.";

    public IReadOnlyList<ConversationAnswerReviewRow> AnswerReviewRows => answerReviewRows;
    public ConversationAnswerReviewRow? SelectedAnswerReview
    {
        get => selectedAnswerReview;
        set
        {
            if (value is not null && !AnswerReviewRows.Any(row => ReferenceEquals(row, value))) { return; }
            if (ReferenceEquals(value, selectedAnswerReview)) { return; }
            selectedAnswerReview = value;
            openedAnswerReview = null;
            answerReviewPresentation = null;
            NotifyAnswerReviews();
        }
    }
    public string AnswerReviewStatus => answerReviewStatus;
    public bool CanNextAnswerReviews => CanReadConversation && answerReviewPage?.Next is not null;
    public bool CanOpenAnswerReview => CanReadConversation && selectedAnswerReview is not null && openConversationDetail is not null;
    private bool AnswerReviewChoiceCurrent => !closed && access.CanInspect && access.CanControl
        && ReferenceEquals(openedAnswerReview, selectedAnswerReview) && answerReviewPresentation?.Invoke() == true;
    public bool CanMarkAnswerReviewed => CanOpenAnswerReview && AnswerReviewChoiceCurrent && selectedAnswerReview?.MarkedReviewed == false;
    public bool CanUnmarkAnswerReviewed => CanOpenAnswerReview && AnswerReviewChoiceCurrent && selectedAnswerReview?.MarkedReviewed == true;

    public Task ReadAnswerReviewsAsync(ConversationAnswerReviewFilter filter, bool next = false) =>
        RunAnswerReviewAsync(async (target, live) =>
        {
            var cursor = next ? answerReviewPage?.Next
                ?? throw new InvalidOperationException("No current answer-review continuation is available.") : null;
            ClearAnswerReviewSelection();
            var page = await service.ReadAnswerReviewsAsync(target, cursor?.Filter ?? filter, cursor, 25, live, lifetime.Token);
            if (!live()) { throw new OperationCanceledException("The exact answer-review selection changed."); }
            SetAnswerReviewPage(page);
            answerReviewStatus = DescribeAnswerReviews(page);
        });

    public Task OpenAnswerReviewAsync(Func<bool> presentation) => RunAnswerReviewAsync(async (_, live) =>
    {
        var page = answerReviewPage ?? throw new InvalidOperationException("Read exact answer-review metadata first.");
        var row = selectedAnswerReview ?? throw new InvalidOperationException("Select one actual answer-review row.");
        ClearAnswerReviewSelection(keepSelected: true);
        bool DetailLive() => live() && presentation() && ReferenceEquals(answerReviewPage, page)
            && ReferenceEquals(selectedAnswerReview, row);
        var content = await service.ReadAnswerReviewDetailAsync(page, row, DetailLive, lifetime.Token);
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Storage);
        async Task<Kora.Core.Presentation.AdmittedDetailContent> Resolve(CancellationToken token)
        {
            using var activity = continuation();
            try
            {
                var exact = await service.ReadAnswerReviewDetailAsync(page, row, DetailLive, token);
                activity.Complete(HostOperationOutcome.Completed);
                return exact;
            }
            catch (Exception exception)
            {
                activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
                throw;
            }
        }
        await (openConversationDetail ?? throw new InvalidOperationException("The canonical Details presenter is unavailable."))(
            content, DetailLive, Resolve, lifetime.Token);
        if (!DetailLive()) { throw new OperationCanceledException("The original answer Details presentation was retired."); }
        openedAnswerReview = row;
        answerReviewPresentation = DetailLive;
        answerReviewStatus = "Exact retained ASSISTANT Details opened. Nothing marked. Choose Mark this answer reviewed, or undo only this answer's existing flag.";
    });

    public Task SetAnswerReviewedAsync(bool reviewed) => CaptureAnswerReviewDecision(reviewed)();

    internal Func<Task> CaptureAnswerReviewDecision(bool reviewed)
    {
        var page = answerReviewPage;
        var row = selectedAnswerReview;
        var presentation = answerReviewPresentation;
        return () => SetAnswerReviewedAsync(page, row, presentation, reviewed);
    }

    private Task SetAnswerReviewedAsync(ConversationAnswerReviewPage? expectedPage,
        ConversationAnswerReviewRow? expectedRow, Func<bool>? expectedPresentation, bool reviewed)
    {
        bool ExpectedChoiceCurrent() => ReferenceEquals(answerReviewPage, expectedPage)
            && ReferenceEquals(selectedAnswerReview, expectedRow) && ReferenceEquals(openedAnswerReview, expectedRow)
            && ReferenceEquals(answerReviewPresentation, expectedPresentation) && AnswerReviewChoiceCurrent;
        return RunAnswerReviewAsync(async (_, live) =>
        {
            var page = expectedPage ?? throw new InvalidOperationException("Read exact answer-review metadata first.");
            var row = expectedRow ?? throw new InvalidOperationException("Select one actual answer-review row.");
            var presentation = expectedPresentation ?? throw new InvalidOperationException("Open this exact answer's current canonical Details first.");
            if (row.MarkedReviewed == reviewed)
            { throw new InvalidOperationException("This exact answer already has the requested review flag."); }
            bool MarkLive() => live() && ExpectedChoiceCurrent() && presentation();
            await service.SetAnswerReviewedAsync(page, row, reviewed, MarkLive, lifetime.Token);
            ClearAnswerReviews();
            var refreshed = await service.ReadAnswerReviewsAsync(page.Target, page.Filter, null, 25, live, lifetime.Token);
            if (!live()) { throw new OperationCanceledException("Answer-review readback presentation was retired."); }
            SetAnswerReviewPage(refreshed);
            answerReviewStatus = (reviewed ? "Only this exact answer marked reviewed. " : "Only this exact answer unmarked. ")
                + DescribeAnswerReviews(refreshed);
        }, ExpectedChoiceCurrent);
    }

    private Task RunAnswerReviewAsync(Func<Kora.Core.Storage.SessionWorkspaceEntry, Func<bool>, Task> operation,
        Func<bool>? expectedChoice = null) =>
        RunConversationAsync(async (target, live) =>
        {
            if (expectedChoice is not null && !expectedChoice())
            { throw new InvalidOperationException("The exact answer-review choice changed; the old control cannot change a replacement answer. Open current canonical Details before a new deliberate choice."); }
            try { await operation(target, live); }
            catch
            {
                ClearAnswerReviews();
                answerReviewStatus = "Unavailable: answer-review source, private admission or durable readback is unconfirmed. "
                    + "No zero count, rollback or successful mark is inferred. Read again explicitly.";
                throw;
            }
            finally { NotifyAnswerReviews(); }
        });

    private void ClearAnswerReviewSelection(bool keepSelected = false)
    {
        if (!keepSelected) { selectedAnswerReview = null; }
        openedAnswerReview = null;
        answerReviewPresentation = null;
    }

    private void ClearAnswerReviews()
    {
        SetAnswerReviewPage(null);
        ClearAnswerReviewSelection();
        answerReviewStatus = "Answer-review state unavailable. Select the exact retained conversation and read again explicitly; no zero count is inferred.";
    }

    private void SetAnswerReviewPage(ConversationAnswerReviewPage? page)
    {
        answerReviewPage = page;
        answerReviewRows = page?.Rows ?? [];
    }

    private static string DescribeAnswerReviews(ConversationAnswerReviewPage page) =>
        $"{page.NotMarkedReviewed} of {page.TotalAnswers} retained answers not marked reviewed at this read. "
        + $"{page.Rows.Length} metadata rows shown; " + (page.Next is null ? "no further matching rows in this snapshot. " : "more matching rows omitted; use Next. ")
        + "New background results require Read again. This is not a read receipt or proof of understanding.";

    private void NotifyAnswerReviews()
    {
        foreach (var name in new[] { nameof(AnswerReviewRows), nameof(SelectedAnswerReview), nameof(AnswerReviewStatus),
            nameof(CanNextAnswerReviews), nameof(CanOpenAnswerReview), nameof(CanMarkAnswerReviewed), nameof(CanUnmarkAnswerReviewed) })
        { OnPropertyChanged(name); }
    }
}
