using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Kora.Application.Infrastructure;
using Kora.Application.Presentation;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class LocalFilePreviewWindow
{
    private TextBox? answerQuestion;
    private LocalFileAnswerSelection? answerSelection;
    private NamedTextBlock? answerText;
    private ListBox? answerCitations;

    private void ShowAnswer(DockPanel body, LocalFileRevision source,
        Func<string, Func<bool>, CancellationToken, Task<LocalFileAnswerResult>> answer, Func<bool> isCurrent)
    {
        var selection = new LocalFileAnswerSelection();
        answerSelection = selection;
        var question = new TextBox
        {
            PlaceholderText = $"Question: {LocalFileRetrievalPolicy.MaximumQueryCharacters} characters / "
                + $"{LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes} UTF-8 bytes; bounded lexical terms only",
        };
        answerQuestion = question;
        AutomationProperties.SetName(question, "Question using only this exact immutable file snapshot");
        var text = new NamedTextBlock { TextWrapping = TextWrapping.Wrap };
        answerText = text;
        AutomationProperties.SetName(text, "Local file answer outcome and cited model claims");
        var rows = new ListBox
        {
            Height = 100,
            ItemTemplate = new FuncDataTemplate<LocalFileCitation>((citation, _) => new NamedTextBlock
            {
                Text = citation is null ? string.Empty : DescribeCitation(citation),
                TextWrapping = TextWrapping.Wrap,
            }),
        };
        answerCitations = rows;
        AutomationProperties.SetName(rows, "Exact source citations from this successful local file answer");
        bool Current() => isCurrent() && ReferenceEquals(answerSelection, selection);
        var navigate = new Button { Content = "Show exact answer citation", IsEnabled = false };
        navigate.Command = new AsyncCommand(() => NavigateCitation(selection.Resolve, rows, Current,
            _ => Task.FromResult<IReadOnlyList<LocalFileRevision>>([source]), _ => { }),
            exception => reportFailure("Answer citation unavailable; no source navigation claimed. Failure type: " + exception.GetType().Name));
        rows.SelectionChanged += (_, _) =>
        {
            ClearNavigation();
            navigate.IsEnabled = Current() && rows.SelectedItem is LocalFileCitation;
        };
        void Invalidate()
        {
            selection.Invalidate();
            text.Text = null;
            rows.ItemsSource = null;
            navigate.IsEnabled = false;
            ClearNavigation();
        }
        question.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty) { Invalidate(); }
        };
        var submit = new Button { Content = "Use this exact snapshot once to answer locally" };
        AutomationProperties.SetName(submit, "Explicit one-question local model source-use request, separate from preview consent");
        submit.Command = new AsyncCommand(async () =>
        {
            Invalidate();
            var result = await selection.Run(question.Text ?? string.Empty, answer, Current);
            if (result is null || !Current()) { return; }
            text.Text = $"Outcome: {result.Outcome}; model reason: {result.ModelReason?.ToString() ?? "(none)"}.\n"
                + $"Matching chunks: {result.MatchingChunks}; lexical selection truncated: {result.RetrievalTruncated}.\n"
                + (result.Outcome switch
                {
                    LocalFileAnswerOutcome.SourceUseUnavailable =>
                        "Unavailable: the required sensitive-content/source-use admission service is not composed. Preview consent is not model-use consent.",
                    LocalFileAnswerOutcome.NoEvidence => "No admitted lexical excerpt supports this question. No model was invoked.",
                    LocalFileAnswerOutcome.Unsupported => "The admitted model response reports no supported answer in the selected evidence.",
                    LocalFileAnswerOutcome.ContextLimit => "Complete serialized context exceeds the host budget. Nothing was silently dropped.",
                    LocalFileAnswerOutcome.Succeeded => "Model-generated claims; exact citation identity is verified, not semantic truth. Verify each source span.\n"
                        + string.Join("\n\n", result.Claims.Select((claim, index) => $"{index + 1}. {claim.Text}\n"
                            + string.Join("\n", claim.Citations.Select(DescribeCitation)))),
                    _ => "No answer claimed. Check source admission, question limits and qualified local runtime, or cancel.",
                });
            rows.ItemsSource = result.Outcome == LocalFileAnswerOutcome.Succeeded
                ? result.Claims.SelectMany(claim => claim.Citations).Distinct().ToArray() : null;
        }, exception =>
        {
            if (!Current()) { return; }
            Invalidate();
            reportFailure("Local file answer unavailable; no answer claimed. Failure type: " + exception.GetType().Name);
        });
        var cancel = new Button { Content = "Cancel file answer and clear claims" };
        cancel.Click += (_, _) => Invalidate();
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new NamedTextBlock
        {
            Text = "ONE current immutable file only: " + DescribeItem(source) + "\n"
                + "Separate explicit request: send bounded exact excerpts and this question only to an admitted LOCAL model. "
                + "Source-use/sensitive-content policy and qualified runtime must independently admit use. "
                + "No hosted fallback, retention, fresh read, folder expansion, tools or speech. Unavailable gates stay closed.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(question);
        panel.Children.Add(submit);
        panel.Children.Add(cancel);
        panel.Children.Add(new ScrollViewer { Content = text, MaxHeight = 120 });
        panel.Children.Add(rows);
        panel.Children.Add(navigate);
        DockPanel.SetDock(panel, Dock.Bottom);
        body.Children.Insert(1, panel);
    }

    internal void FocusAnswer()
    {
        Activate();
        answerQuestion?.Focus();
    }

    private void ClearAnswer()
    {
        answerSelection?.Dispose();
        answerSelection = null;
        if (answerQuestion is not null) { answerQuestion.Text = null; }
        if (answerText is not null) { answerText.Text = null; }
        if (answerCitations is not null) { answerCitations.ItemsSource = null; }
        answerQuestion = null;
        answerText = null;
        answerCitations = null;
    }
}
