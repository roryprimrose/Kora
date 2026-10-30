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
    private ListBox? folderAnswerCandidates;
    private FolderAnswerReview? folderAnswerReview;
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
        var candidates = new ListBox
        {
            Height = 140,
            SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
            ItemTemplate = rows.ItemTemplate,
        };
        folderAnswerCandidates = candidates;
        AutomationProperties.SetName(candidates, "Choose whole retrieved folder excerpts for model context; none selected by default");
        var reviewButton = new Button { Content = "Review folder excerpts for this question" };
        var reviewChosen = new Button { Content = "Review only chosen folder excerpts", IsEnabled = false };
        var confirm = new Button { Content = "Confirm: answer locally using only these reviewed folder excerpts", IsEnabled = false };
        var navigate = new Button { Content = "Show exact folder answer citation", IsEnabled = false };
        FolderAnswerReview? reviewed = null;
        var busy = false;
        var confirmed = false;
        bool Current() => isCurrent() && ReferenceEquals(folderAnswerSelection, selection);
        void InvalidateChosen()
        {
            folderAnswerGeneration++;
            folderAnswerCancellation?.Cancel();
            selection.Invalidate();
            folderAnswerReview?.InvalidateSelection();
            reviewed = null;
            confirm.IsEnabled = false;
            navigate.IsEnabled = false;
            status.Text = null;
            rows.ItemsSource = null;
            ClearNavigation();
        }
        void Invalidate()
        {
            InvalidateChosen();
            folderAnswerReview?.Invalidate();
            folderAnswerReview = null;
            candidates.ItemsSource = null;
            reviewChosen.IsEnabled = false;
        }
        candidates.SelectionChanged += (_, _) =>
        {
            InvalidateChosen();
            reviewChosen.IsEnabled = !busy && !confirmed && Current() && candidates.SelectedItems?.Count > 0;
        };
        question.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty) { Invalidate(); }
        };
        reviewButton.Command = new AsyncCommand(async () =>
        {
            if (busy || !Current()) { return; }
            Invalidate();
            confirmed = false;
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
                folderAnswerReview = result.Review;
                candidates.ItemsSource = result.Review?.RetrievedSearch.Citations;
                status.Text = result.Review is not { } retrieved
                    ? $"Folder excerpt review: {result.Outcome}. No model was invoked; no answer claimed."
                    : $"Choose whole excerpts from {retrieved.Search.Citations.Count} retrieved citations, then review your chosen set. "
                        + "None is selected for model context by default. Highlighting text or Find matches grants no context authority.\n"
                        + $"Total matching chunks: {retrieved.Search.MatchingChunks}; bounded retrieval omitted other matches: {retrieved.Search.Truncated}.";
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
        reviewChosen.Command = new AsyncCommand(() =>
        {
            if (busy || confirmed || !Current() || folderAnswerReview is not { } retrieved) { return Task.CompletedTask; }
            InvalidateChosen();
            reviewed = retrieved.SelectEvidence(candidates.SelectedItems!.Cast<LocalFileCitation>().ToArray());
            status.Text = $"REVIEW NEEDED. Original question (unchanged):\n{reviewed.Question}\n"
                + $"User-selected scope only: {reviewed.Search.Citations.Count} selected excerpts / {reviewed.ExcerptUtf8Bytes} UTF-8 bytes "
                + $"(global limits: {LocalFileRetrievalPolicy.MaximumCitations} citations / {LocalFileRetrievalPolicy.MaximumExcerptUtf8Bytes} excerpt bytes; "
                + $"{ModelContextEnvelope.MaximumInputUtf8Bytes} complete serialized bytes; excess is rejected, never truncated).\n"
                + $"User omitted {reviewed.RetrievedSearch.Citations.Count - reviewed.Search.Citations.Count} retrieved excerpts. "
                + $"Total matching chunks: {reviewed.RetrievedSearch.MatchingChunks}; bounded retrieval omitted other matches: {reviewed.RetrievedSearch.Truncated}.\n"
                + "Complete chosen excerpts and exact source/revision/digest/UTF-16 locations are below. "
                + "The answer cannot claim review of omitted sections or the full folder.\n"
                + LocalContextSessionTarget.FreshSessionDisclosure + "\n"
                + $"Reviewed session: {reviewed.Target.Request.SessionId.Value:D}; task: {reviewed.Target.Request.TaskId.Value:D}. "
                + $"Original context deadline (not renewed by selection): {reviewed.ExpiresAt:O}.\n"
                + "Confirm separately for ONE LocalOnly tool-free answer using only this unchanged question and chosen evidence. "
                + LocalSourceRiskPolicy.Disclosure
                + " Choice is not risk approval: the whole original evidence-bearing file, question and excerpts still require source-use admission. "
                + "No hosted fallback, tools, speech, persistence or fresh reads.";
            rows.ItemsSource = reviewed.Search.Citations;
            confirm.IsEnabled = reviewed.IsCurrent(DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }, exception =>
        {
            InvalidateChosen();
            reportFailure("Chosen folder excerpt review rejected; no model invoked. Failure type: " + exception.GetType().Name);
        });
        confirm.Command = new AsyncCommand(async () =>
        {
            if (busy || reviewed is not { } exactReview || !Current()) { return; }
            if (!exactReview.IsCurrent(DateTimeOffset.UtcNow))
            {
                InvalidateChosen();
                status.Text = "The original review expired or was revoked. No model was invoked. Search and review again explicitly.";
                return;
            }
            var generation = folderAnswerGeneration;
            bool Admitted() => Current() && generation == folderAnswerGeneration && exactReview.IsCurrent(DateTimeOffset.UtcNow);
            confirmed = true;
            confirm.IsEnabled = false;
            reviewButton.IsEnabled = false;
            reviewChosen.IsEnabled = false;
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
                        LocalFileAnswerOutcome.SourceUseUnavailable => "Required source-use risk admission is unavailable. Preview consent is not model-use consent.",
                        LocalFileAnswerOutcome.Denied when result.SourceUseDecision == LocalFileModelUseDecision.Denied =>
                            "Denied: credential-like risk or invalid exact source evidence. No override or model request is admitted.",
                        LocalFileAnswerOutcome.ContextLimit => "The complete serialized context exceeds the host limit. Nothing was silently dropped.",
                        _ => "No answer claimed. Review again only after checking source admission, question and local runtime.",
                    })
                    + $"\nUser-selected excerpts only: {exactReview.Search.Citations.Count}; "
                    + $"user omitted {exactReview.RetrievedSearch.Citations.Count - exactReview.Search.Citations.Count} retrieved excerpts. "
                    + $"Matching chunks: {exactReview.RetrievedSearch.MatchingChunks}; retrieval omitted other matches: {exactReview.RetrievedSearch.Truncated}. "
                    + "No claim of reviewing omitted sections or the full folder. "
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
        bool AnswerCurrent() => Current() && reviewed is { } exact && exact.IsCurrent(DateTimeOffset.UtcNow);
        navigate.Command = new AsyncCommand(() => NavigateCitation(selection.Resolve, rows, AnswerCurrent,
            _ => Task.FromResult(source.Files), selectFile),
            exception => reportFailure("Exact folder answer citation unavailable. Failure type: " + exception.GetType().Name));
        rows.SelectionChanged += (_, _) =>
        {
            ClearNavigation();
            navigate.IsEnabled = AnswerCurrent() && !busy && rows.SelectedItem is LocalFileCitation;
        };
        var cancel = new Button { Content = "Cancel folder answer and clear excerpts" };
        cancel.Click += (_, _) => Invalidate();
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(question);
        panel.Children.Add(reviewButton);
        panel.Children.Add(candidates);
        panel.Children.Add(reviewChosen);
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
        folderAnswerReview?.Invalidate();
        folderAnswerReview = null;
        folderAnswerSelection = null;
        if (folderAnswerQuestion is not null) { folderAnswerQuestion.Text = null; }
        if (folderAnswerText is not null) { folderAnswerText.Text = null; }
        if (folderAnswerCitations is not null) { folderAnswerCitations.ItemsSource = null; }
        if (folderAnswerCandidates is not null) { folderAnswerCandidates.ItemsSource = null; }
        folderAnswerQuestion = null;
        folderAnswerText = null;
        folderAnswerCitations = null;
        folderAnswerCandidates = null;
    }
}
