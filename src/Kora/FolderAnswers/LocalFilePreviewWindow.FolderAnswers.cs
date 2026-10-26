using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Kora.Application.Infrastructure;
using Kora.Application.Presentation;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Context.FolderAnswers;
using Kora.Core.Dependencies;

namespace Kora;

internal sealed partial class LocalFilePreviewWindow
{
    private CancellationTokenSource? folderAnswerCancellation;
    private LocalFileAnswerSelection? folderAnswerSelection;
    private TextBox? folderAnswerQuestion;
    private NamedTextBlock? folderAnswerText;
    private ListBox? folderAnswerCitations;
    private long folderAnswerGeneration;

    private void ShowFolderAnswer(DockPanel body, LocalFolderRevision source,
        Func<string, Func<bool>, CancellationToken, Task<FolderAnswerPreparation>> prepare,
        Func<FolderAnswerReview, Func<bool>, CancellationToken, Task<LocalFileAnswerResult>> answer,
        Func<bool> isCurrent, Action<LocalFileRevision> selectFile)
    {
        var selection = new LocalFileAnswerSelection();
        folderAnswerSelection = selection;
        var question = new TextBox { PlaceholderText = "Ask one bounded lexical question across this exact folder snapshot" };
        folderAnswerQuestion = question;
        AutomationProperties.SetName(question, "Question across the exact immutable folder snapshot");
        var status = new NamedTextBlock { TextWrapping = TextWrapping.Wrap };
        folderAnswerText = status;
        AutomationProperties.SetName(status, "Folder answer review and local model outcome");
        var rows = new ListBox
        {
            Height = 140,
            ItemTemplate = new FuncDataTemplate<LocalFileCitation>((citation, _) => new NamedTextBlock
            {
                Text = citation is null ? string.Empty : DescribeCitation(citation),
                TextWrapping = TextWrapping.Wrap,
            }),
        };
        folderAnswerCitations = rows;
        AutomationProperties.SetName(rows, "Complete selected folder excerpts and exact citations");
        var reviewButton = new Button { Content = "Review folder excerpts for this question" };
        var confirm = new Button { Content = "Confirm: answer locally using only these reviewed folder excerpts", IsEnabled = false };
        var navigate = new Button { Content = "Show exact folder answer citation", IsEnabled = false };
        FolderAnswerReview? reviewed = null;
        var busy = false;
        bool Current() => isCurrent() && ReferenceEquals(folderAnswerSelection, selection);
        void Invalidate()
        {
            folderAnswerGeneration++;
            folderAnswerCancellation?.Cancel();
            selection.Invalidate();
            reviewed = null;
            confirm.IsEnabled = false;
            navigate.IsEnabled = false;
            status.Text = null;
            rows.ItemsSource = null;
            ClearNavigation();
        }
        question.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty) { Invalidate(); }
        };
        reviewButton.Command = new AsyncCommand(async () =>
        {
            if (busy || !Current()) { return; }
            Invalidate();
            var generation = folderAnswerGeneration;
            bool Admitted() => Current() && generation == folderAnswerGeneration;
            busy = true;
            reviewButton.IsEnabled = false;
            using var cancellation = new CancellationTokenSource();
            folderAnswerCancellation = cancellation;
            try
            {
                var result = await prepare(question.Text ?? string.Empty, Admitted, cancellation.Token);
                if (!Admitted() || cancellation.IsCancellationRequested) { return; }
                reviewed = result.Review;
                status.Text = reviewed is null ? $"Folder excerpt review: {result.Outcome}. No model was invoked; no answer claimed."
                    : $"REVIEW NEEDED. {reviewed.Search.Citations.Count} selected excerpts / {reviewed.ExcerptUtf8Bytes} UTF-8 bytes.\n"
                        + $"Total matching chunks: {reviewed.Search.MatchingChunks}; bounded retrieval omitted other matches: {reviewed.Search.Truncated}.\n"
                        + "All selected excerpts and exact locations are listed below, without display truncation. This is not whole-folder model context.\n"
                        + LocalContextSessionTarget.FreshSessionDisclosure + "\n"
                        + $"Reviewed session: {reviewed.Target.Request.SessionId.Value:D}; task: {reviewed.Target.Request.TaskId.Value:D}.\n"
                        + "Confirm separately to send only this question and these excerpts to the admitted local model. "
                        + "Source-use/sensitivity policy and runtime must independently allow use. No hosted fallback, tools, speech, persistence or fresh reads.";
                rows.ItemsSource = reviewed?.Search.Citations;
                confirm.IsEnabled = reviewed is not null;
            }
            finally
            {
                if (ReferenceEquals(folderAnswerCancellation, cancellation)) { folderAnswerCancellation = null; }
                busy = false;
                reviewButton.IsEnabled = Current();
            }
        }, exception =>
        {
            if (!Current()) { return; }
            Invalidate();
            reportFailure("Folder excerpt review unavailable; no model invoked. Failure type: " + exception.GetType().Name);
        });
        confirm.Command = new AsyncCommand(async () =>
        {
            if (busy || reviewed is not { } exactReview || !Current()) { return; }
            var generation = folderAnswerGeneration;
            bool Admitted() => Current() && generation == folderAnswerGeneration;
            reviewed = null;
            confirm.IsEnabled = false;
            reviewButton.IsEnabled = false;
            status.Text = null;
            rows.ItemsSource = null;
            ClearNavigation();
            busy = true;
            try
            {
                var result = await selection.Run(exactReview.Question,
                    (_, current, token) => answer(exactReview, current, token), Admitted);
                if (result is null || !Admitted()) { return; }
                status.Text = $"Folder answer: {result.Outcome}; source-use: {result.SourceUseDecision}; execution: {result.ExecutionReason}.\n"
                    + (result.Outcome switch
                    {
                        LocalFileAnswerOutcome.Succeeded => "Model-generated claims; citation identity is verified, not semantic truth. Verify each exact source span.\n"
                            + string.Join("\n\n", result.Claims.Select((claim, index) => $"{index + 1}. {claim.Text}\n"
                                + string.Join("\n", claim.Citations.Select(DescribeCitation)))),
                        LocalFileAnswerOutcome.Unsupported => "The local model reports no supported answer in the reviewed evidence.",
                        LocalFileAnswerOutcome.SourceUseUnavailable => "Required source-use/sensitive-content policy is unavailable. Preview consent is not model-use consent.",
                        LocalFileAnswerOutcome.ContextLimit => "The complete serialized context exceeds the host limit. Nothing was silently dropped.",
                        _ => "No answer claimed. Review again only after checking source admission, question and local runtime.",
                    })
                    + $"\nMatching chunks: {exactReview.Search.MatchingChunks}; retrieval omitted other matches: {exactReview.Search.Truncated}. "
                    + "This confirmation is consumed; no automatic retry.";
                rows.ItemsSource = result.Outcome == LocalFileAnswerOutcome.Succeeded
                    ? result.Claims.SelectMany(claim => claim.Citations).Distinct().ToArray() : null;
            }
            finally { busy = false; reviewButton.IsEnabled = Current(); }
        }, exception =>
        {
            if (!Current()) { return; }
            Invalidate();
            reportFailure("Folder answer unavailable; no answer claimed. Failure type: " + exception.GetType().Name);
        });
        navigate.Command = new AsyncCommand(() => NavigateCitation(selection.Resolve, rows, Current,
            _ => Task.FromResult(source.Files), selectFile),
            exception => reportFailure("Exact folder answer citation unavailable. Failure type: " + exception.GetType().Name));
        rows.SelectionChanged += (_, _) =>
        {
            ClearNavigation();
            navigate.IsEnabled = Current() && reviewed is null && !busy && rows.SelectedItem is LocalFileCitation;
        };
        var cancel = new Button { Content = "Cancel folder answer and clear excerpts" };
        cancel.Click += (_, _) => Invalidate();
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(question);
        panel.Children.Add(reviewButton);
        panel.Children.Add(confirm);
        panel.Children.Add(cancel);
        panel.Children.Add(new ScrollViewer { Content = status, MaxHeight = 140 });
        panel.Children.Add(rows);
        panel.Children.Add(navigate);
        DockPanel.SetDock(panel, Dock.Bottom);
        body.Children.Insert(1, panel);
    }

    private void ClearFolderAnswer()
    {
        folderAnswerGeneration++;
        folderAnswerCancellation?.Cancel();
        folderAnswerSelection?.Dispose();
        folderAnswerSelection = null;
        if (folderAnswerQuestion is not null) { folderAnswerQuestion.Text = null; }
        if (folderAnswerText is not null) { folderAnswerText.Text = null; }
        if (folderAnswerCitations is not null) { folderAnswerCitations.ItemsSource = null; }
        folderAnswerQuestion = null;
        folderAnswerText = null;
        folderAnswerCitations = null;
    }
}
