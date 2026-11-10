using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Kora.Application.Context;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class ClipboardPreviewView
{
    private ClipboardQuestionReview? questionReview;
    private CancellationTokenSource? questionCancellation;
    private Task questionWork = Task.CompletedTask;
    private TextBox? questionInput;
    private TextBox? questionReviewText;
    private CheckBox? questionConsent;
    private NamedTextBlock? questionStatus;
    private long questionGeneration;

    private void AddQuestion(StackPanel content, ClipboardSnapshot snapshot, Func<bool> isCurrent,
        ClipboardQuestionActions actions, ScrollViewer viewport)
    {
        var generation = questionGeneration;
        var panel = new StackPanel { Spacing = 8 };
        var input = QuestionTextBox("Original question about this same clipboard snapshot", false);
        input.AcceptsReturn = true;
        input.TextWrapping = TextWrapping.Wrap;
        input.MinHeight = 60;
        questionInput = input;
        var reviewText = QuestionTextBox("Full exact clipboard question and source review; LocalOnly destination", true);
        reviewText.TextWrapping = TextWrapping.Wrap;
        reviewText.MaxHeight = 240;
        questionReviewText = reviewText;
        var consent = new CheckBox { Content = new NamedTextBlock
        {
            Text = "I reviewed the FULL source and unchanged question. Confirm ONE LocalOnly, no-tools use.",
            TextWrapping = TextWrapping.Wrap,
        } };
        AutomationProperties.SetName(consent, "Confirm one use of this exact clipboard question and source");
        questionConsent = consent;
        var status = new NamedTextBlock
        {
            Text = "Type a new original question. Review reads no clipboard and sends nothing. No previous answer or history is included.",
            TextWrapping = TextWrapping.Wrap,
        };
        questionStatus = status;
        AutomationProperties.SetName(status, "Same-snapshot clipboard question outcome");
        bool Current() => generation == questionGeneration && isCurrent();
        input.PropertyChanged += (_, args) =>
        {
            if (args.Property != TextBox.TextProperty || generation != questionGeneration) { return; }
            RetireQuestionReview();
            status.Text = "Question changed. Review and separately confirm again; no old review can be reused.";
        };
        var reviewButton = new Button { Content = "Review question and same snapshot" };
        reviewButton.Command = new AsyncCommand(() =>
        {
            if (!Current()) { throw new InvalidOperationException("The original clipboard view is no longer current."); }
            RetireQuestionReview();
            if (!questionWork.IsCompleted)
            { throw new InvalidOperationException("The previous local operation must finish releasing resources first."); }
            var review = actions.Review(input.Text ?? string.Empty);
            if (!Current() || !ReferenceEquals(review.Source, snapshot) || !review.IsCurrent())
            {
                review.Retire();
                throw new InvalidOperationException("The exact question/source review is no longer current.");
            }
            questionReview = review;
            reviewText.Text = "Destination: LocalOnly. Tools: none. Volatile answer only; no history, speech or previous answer.\n"
                + "The original capture's 15-minute lifetime is unchanged. The clipboard.explain-local permission is separate.\n\n"
                + "EXACT ORIGINAL QUESTION:\n" + review.Question + "\n\nCOMPLETE UNTRUSTED SNAPSHOT:\n" + snapshot.Text;
            status.Text = "Review the complete question and source, then separately confirm this one use.";
            reviewText.Focus();
            return Task.CompletedTask;
        }, Failure);
        var confirm = new Button { Content = "Ask locally using this same snapshot" };
        confirm.Command = new AsyncCommand(async () =>
        {
            if (!Current() || questionReview is not { } review || !review.IsCurrent()
                || consent.IsChecked != true || !questionWork.IsCompleted)
            { throw new InvalidOperationException("A fresh exact question/source review and separate one-use confirmation are required."); }
            consent.IsChecked = false;
            using var cancellation = new CancellationTokenSource();
            questionCancellation = cancellation;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            questionWork = completion.Task;
            try
            {
                status.Text = "One LocalOnly request is running. Cancel retires disclosure; owned work must actually join.";
                var result = await actions.Answer(review, cancellation.Token);
                if (!Current() || !ReferenceEquals(questionReview, review)) { return; }
                status.Text = "Local question outcome: " + result.Outcome + ". "
                    + (result.ModelReason is { } reason ? "Model reason: " + reason + ". " : string.Empty)
                    + "Review consumed; no automatic retry or recapture. Only an admitted answer appears above.";
            }
            finally
            {
                if (ReferenceEquals(questionCancellation, cancellation)) { questionCancellation = null; }
                completion.SetResult();
            }
        }, Failure);
        var cancel = new Button { Content = "Cancel clipboard question" };
        cancel.Command = new AsyncCommand(async () =>
        {
            if (generation != questionGeneration) { return; }
            RetireQuestionReview();
            input.Text = null;
            status.Text = "Question cancelled. Waiting for actual owned work to finish; no retry is queued.";
            var outstanding = questionWork;
            await outstanding;
            if (generation == questionGeneration) { status.Text = "Question cancelled; owned work joined. Review again for any new use."; }
        }, Failure);
        panel.Children.Add(status);
        panel.Children.Add(input);
        panel.Children.Add(reviewButton);
        panel.Children.Add(reviewText);
        panel.Children.Add(consent);
        panel.Children.Add(confirm);
        panel.Children.Add(cancel);
        var expander = new Expander
        {
            Header = "Ask a question about this same snapshot", Content = panel,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        expander.Bind(MaxWidthProperty, new Binding("Viewport.Width") { Source = viewport });
        content.Children.Add(expander);

        void Failure(Exception exception)
        {
            if (generation == questionGeneration)
            {
                RetireQuestionReview();
                status.Text = "Question refused or unavailable. Nothing was recaptured or retried. "
                    + "Enter nonempty, well-formed original text within 4096 UTF-16 / 16384 UTF-8 bytes; credentials are not accepted.";
            }
            reportFailure(exception);
        }
    }

    private static TextBox QuestionTextBox(string name, bool readOnly)
    {
        var control = new TextBox { IsReadOnly = readOnly, ContextMenu = null };
        AutomationProperties.SetName(control, name);
        control.AddHandler(TextBox.CopyingToClipboardEvent, BlockClipboardTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        control.AddHandler(TextBox.CuttingToClipboardEvent, BlockClipboardTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        control.AddHandler(TextBox.PastingFromClipboardEvent, BlockClipboardTransfer, RoutingStrategies.Direct | RoutingStrategies.Bubble | RoutingStrategies.Tunnel);
        return control;
    }

    private void RefreshQuestion()
    {
        if (questionReview is { } review && !review.IsCurrent())
        {
            RetireQuestionReview();
            if (questionInput is not null) { questionInput.Text = null; }
            if (questionStatus is not null) { questionStatus.Text = "Question review retired: source, expiry or original host admission changed. No recapture."; }
        }
    }

    private void RetireQuestionReview()
    {
        questionReview?.Retire();
        questionReview = null;
        questionCancellation?.Cancel();
        if (questionConsent is not null) { questionConsent.IsChecked = false; }
        if (questionReviewText is not null) { questionReviewText.Text = null; }
    }

    private void ClearQuestion()
    {
        questionGeneration++;
        RetireQuestionReview();
        if (questionInput is not null) { questionInput.Text = null; }
        questionInput = null;
        questionReviewText = null;
        questionConsent = null;
        questionStatus = null;
    }
}
