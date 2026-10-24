using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Conversation;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class LocalFilePreviewWindow
{
    private SessionAttachmentAnswerReview? attachmentAnswerReview;
    private CancellationTokenSource? attachmentAnswerCancellation;
    private Action? clearAttachmentAnswer;
    private DockPanel? attachmentAnswerBody;

    internal void ShowAttachmentAnswer(SessionFileAttachment source,
        Func<string, Func<bool>, CancellationToken, Task<SessionAttachmentAnswerReview>> prepare,
        Func<SessionAttachmentAnswerReview, Func<bool>, CancellationToken, Task<ConversationSubmissionReceipt>> submit,
        Func<bool> current, CancellationToken token)
    {
        if (attachmentAnswerBody is not { } body || findSource?.Reference != source.File.Reference)
        { throw new InvalidOperationException("The exact retained attachment must be displayed before answer review."); }
        var question = new TextBox { PlaceholderText = "Question about this exact retained source (bounded lexical terms)" };
        var evidence = new NamedTextBlock { TextWrapping = TextWrapping.Wrap };
        var status = new NamedTextBlock { TextWrapping = TextWrapping.Wrap };
        var reviewButton = new Button { Content = "Review exact evidence for this question" };
        var confirm = new Button { Content = "Consent once: answer locally and retain in this conversation", IsEnabled = false };
        var cancel = new Button { Content = "Cancel / clear attachment answer review" };
        AutomationProperties.SetName(question, "Question using only this exact session's retained attachment; no automatic model disclosure");
        AutomationProperties.SetName(evidence, "Complete admitted evidence excerpts and question for separate one-turn model-use consent");
        void Clear()
        {
            attachmentAnswerCancellation?.Cancel();
            attachmentAnswerCancellation?.Dispose();
            attachmentAnswerCancellation = null;
            attachmentAnswerReview?.Dispose();
            attachmentAnswerReview = null;
            confirm.IsEnabled = false;
            evidence.Text = null;
            status.Text = null;
        }
        clearAttachmentAnswer = () => { Clear(); question.Text = null; };
        question.TextChanged += (_, _) => Clear();
        cancel.Click += (_, _) => Clear();
        reviewButton.Command = new AsyncCommand(async () =>
        {
            Clear();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            attachmentAnswerCancellation = cancellation;
            bool Live() => current() && ReferenceEquals(attachmentAnswerCancellation, cancellation)
                && !cancellation.IsCancellationRequested;
            var review = await prepare(question.Text ?? string.Empty, Live, cancellation.Token);
            if (!Live()) { review.Dispose(); return; }
            attachmentAnswerReview = review;
            evidence.Text = "Question:\n" + review.Question + "\n\nExact lexical evidence (no dropped matching chunks):\n"
                + string.Join("\n\n", review.Evidence.Citations.Select(citation =>
                    $"{citation.StartLine}:{citation.StartColumn}-{citation.EndLine}:{citation.EndColumn}\n{citation.Excerpt}"));
            status.Text = "Review all excerpts above. Separate consent sends only this question and these excerpts to one admitted LOCAL, tool-free model turn. "
                + "The question and verified answer/references are retained in this exact conversation under its existing retention/Keep policy. "
                + "Stored-copy consent did not authorize this disclosure. No original file is reopened. No hosted fallback or tools. "
                + "Unqualified runtime and sensitive-source policy remain unavailable.";
            confirm.IsEnabled = true;
        }, exception =>
        {
            Clear();
            if (current()) { status.Text = "Evidence review unavailable; nothing sent. Failure type: " + exception.GetType().Name; }
        });
        confirm.Command = new AsyncCommand(async () =>
        {
            var review = attachmentAnswerReview ?? throw new InvalidOperationException("Review exact evidence first.");
            var cancellation = attachmentAnswerCancellation ?? throw new InvalidOperationException("The review was revoked.");
            bool Live() => current() && ReferenceEquals(attachmentAnswerReview, review)
                && ReferenceEquals(attachmentAnswerCancellation, cancellation) && !cancellation.IsCancellationRequested;
            confirm.IsEnabled = false;
            reviewButton.IsEnabled = false;
            question.IsReadOnly = true;
            try
            {
                var receipt = await submit(review, Live, cancellation.Token);
                if (!Live()) { return; }
                evidence.Text = null;
                status.Text = receipt.Message.AssistantResponse?.Body
                    ?? $"No answer retained. HOST {receipt.Message.Outcome} / {receipt.Message.Reason}.";
                status.Text += "\nOne-shot consent consumed. No automatic retry. Read the exact conversation for durable status.";
            }
            finally
            {
                review.Dispose();
                if (current()) { question.IsReadOnly = false; reviewButton.IsEnabled = true; }
            }
        }, exception =>
        {
            Clear();
            if (current())
            {
                status.Text = exception is SessionAttachmentAnswerUnavailableException
                    ? exception.Message + " No model turn or conversation retention was started."
                    : "No answer claimed; completion may be unconfirmed. Inspect exact conversation/task receipts without retry. Failure type: "
                        + exception.GetType().Name;
            }
        });
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new NamedTextBlock
        {
            Text = "Answer from THIS retained session attachment only. Inspect the immutable source, enter a question, review admitted excerpts, then separately consent.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(question);
        panel.Children.Add(reviewButton);
        panel.Children.Add(new ScrollViewer { Content = evidence, MaxHeight = 180 });
        panel.Children.Add(confirm);
        panel.Children.Add(cancel);
        panel.Children.Add(new ScrollViewer { Content = status, MaxHeight = 180 });
        DockPanel.SetDock(panel, Dock.Bottom);
        body.Children.Insert(1, panel);
    }

    private void ClearAttachmentAnswer()
    {
        clearAttachmentAnswer?.Invoke();
        clearAttachmentAnswer = null;
        attachmentAnswerBody = null;
    }
}
