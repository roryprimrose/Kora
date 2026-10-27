using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using Kora.Application.Infrastructure;
using Kora.Controls;

namespace Kora;

internal sealed partial class EvidenceWindow
{
    private void BindEvidenceAnswers(EvidenceViewModel model)
    {
        if (!model.HasEvidenceAnswers) { EvidenceRecordBody.Children.Remove(AskEvidence); return; }
        EvidenceRestrictions.Text = "No copy, export, remote model, browser, deletion or execution.";
        var question = new TextBox
        {
            PlaceholderText = "Original question: max 256 characters / 512 UTF-8 bytes",
            AcceptsReturn = false,
        };
        question.Bind(TextBox.TextProperty, new Binding(nameof(EvidenceViewModel.AnswerQuestion)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(question, "Original question about this exact diagnostic page");
        var review = new Button { Content = "Review complete page and question" };
        review.Bind(IsEnabledProperty, new Binding(nameof(EvidenceViewModel.CanReviewEvidenceAnswer)));
        review.Click += (_, _) => model.ReviewEvidenceAnswer();
        var confirm = new Button { Content = "Confirm one tool-free local answer in a new conversation" };
        confirm.Bind(IsEnabledProperty, new Binding(nameof(EvidenceViewModel.CanConfirmEvidenceAnswer)));
        confirm.Command = new AsyncCommand(model.ConfirmEvidenceAnswerAsync,
            _ => model.CancelEvidenceAnswer());
        var cancel = new Button { Content = "Cancel evidence answer and clear" };
        cancel.Click += (_, _) => model.CancelEvidenceAnswer();
        var reviewText = Text(nameof(EvidenceViewModel.AnswerReviewText), "Complete immutable page and local answer disclosure");
        var status = Text(nameof(EvidenceViewModel.AnswerStatus), "Evidence answer review and execution outcome");
        var answer = Text(nameof(EvidenceViewModel.AnswerText), "Validated cited local evidence answer");
        var citations = new ComboBox();
        citations.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(EvidenceViewModel.AnswerCitations)));
        AutomationProperties.SetName(citations, "Exact stable record citations in the reviewed page");
        var show = new Button { Content = "Show exact inert record (no source read)" };
        show.Click += (_, _) =>
        {
            if (citations.SelectedItem is string citation) { model.ShowEvidenceAnswerCitation(citation); }
        };
        var record = Text(nameof(EvidenceViewModel.AnswerCitationText), "Exact inert cited diagnostic record");
        var scales = new[] { reviewText, status, answer, record }.Select(NativeReaderTypography.Capture).ToArray();
        void Scale()
        {
            var percent = DataContext is EvidenceViewModel { IsClosed: false } && model.HasEvidenceAnswers
                ? typographyHostForEvidenceAnswers?.ReaderTextScalePercent ?? 100 : 100;
            foreach (var scale in scales) { scale(percent); }
        }
        foreach (var item in new Control[] { question, review, reviewText, confirm, cancel, status, answer, citations, show, record })
        { EvidenceAnswerPanel.Children.Add(item); }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => { model.RefreshEvidenceAnswerAdmission(); Scale(); };
        timer.Start();
        Closed += (_, _) => { timer.Stop(); model.CancelEvidenceAnswer(); };

        static NamedTextBlock Text(string property, string name)
        {
            var text = new NamedTextBlock { TextWrapping = TextWrapping.Wrap, Focusable = true };
            text.Bind(TextBlock.TextProperty, new Binding(property));
            AutomationProperties.SetName(text, name);
            return text;
        }
    }

    private readonly Kora.Application.ViewModels.MainViewModel? typographyHostForEvidenceAnswers;
}
