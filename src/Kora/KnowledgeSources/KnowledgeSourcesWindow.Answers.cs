using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Kora.Application.Hosting;
using Kora.Application.KnowledgeSources;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;

namespace Kora.KnowledgeSources;

internal sealed partial class KnowledgeSourcesWindow
{
    private readonly KnowledgeSourceAnswerWorkflow? answers;
    private readonly TextBox answerQuestion = new() { PlaceholderText = "One new question using only the selected enabled source" };
    private readonly TextBlock answerStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel answerLinks = new() { Spacing = 4 };
    private readonly StackPanel answerChoices = new() { Spacing = 4 };
    private KnowledgeSourceAnswerReview? retrievedAnswer;
    private KnowledgeSourceAnswerReview? answerReview;
    private CancellationTokenSource? answerCancellation;
    private HostRequest? entryRequest;
    private Func<bool>? entryCurrent;
    private long answerEpoch;

    private void BindAnswers()
    {
        AutomationProperties.SetName(answerQuestion, "New named-source answer question");
        AutomationProperties.SetName(answerStatus, "Named-source answer review and outcome");
        body.Children.Add(answerQuestion);
        body.Children.Add(Button("Review selected named source excerpts for this question", PrepareAnswerAsync));
        body.Children.Add(answerStatus);
        body.Children.Add(answerChoices);
        body.Children.Add(answerLinks);
        answerQuestion.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
            {
                RetireAnswer();
                entryRequest = null;
                entryCurrent = null;
            }
        };
    }

    internal async Task OpenAnswerAsync(KnowledgeSourceAnswerCommand command, Func<bool> current)
    {
        if (!current() || !Eligible()) { throw new InvalidOperationException("Original source-answer input expired."); }
        var request = HostActivity.RequireCurrent().Request;
        await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, LoadAsync);
        if (!current() || !Eligible()) { throw new InvalidOperationException("Original source-answer input was revoked."); }
        var choices = ((IEnumerable<KnowledgeSource>)sources.ItemsSource!).Where(item =>
            item.State == KnowledgeSourceState.Enabled && string.Equals(item.Name, command.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        sources.SelectedItem = choices.Length == 1 ? choices[0] : null;
        answerQuestion.Text = command.Question;
        entryRequest = request;
        entryCurrent = current;
        answerStatus.Text = choices.Length == 1
            ? "Selected the exact enabled source. Review the retained revision and excerpts, then confirm one use separately. No model invoked."
            : "No unique enabled exact name. Explicitly select one source-qualified inventory row, then review. No all-source search or model invoked.";
        Activate();
    }

    private async Task PrepareAnswerAsync()
    {
        if (answers is null) { throw new InvalidOperationException("Named-source local answering is unavailable in this host."); }
        var source = Selected();
        var question = answerQuestion.Text ?? string.Empty;
        var request = entryRequest ?? HostActivity.RequireCurrent().Request;
        var originalCurrent = entryCurrent ?? host.CaptureKnowledgeSourceAnswerEligibility(request.Origin);
        RetireAnswer();
        var generation = Interlocked.Read(ref answerEpoch);
        bool Current() => Eligible() && generation == Interlocked.Read(ref answerEpoch) && originalCurrent();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        answerCancellation = cancellation;
        try
        {
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            var prepared = await answers.PrepareAsync(source, question, Current, cancellation.Token);
            if (!Current()) { return; }
            answerReview = prepared.Review;
            activity.Complete(prepared.Review is not null || prepared.Outcome == LocalFileAnswerOutcome.NoEvidence
                ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            if (prepared.Review is not { } exact)
            {
                answerStatus.Text = $"Named-source review: {prepared.Outcome}. No model invoked; no supported answer claimed.";
                return;
            }
            retrievedAnswer = exact;
            ShowAnswerChoices(exact, Current);
            ShowAnswerReview(exact, Current);
        }
        finally { answerCancellation = null; }
    }

    private void ShowAnswerChoices(KnowledgeSourceAnswerReview retrieved, Func<bool> current)
    {
        var candidates = new ListBox
        {
            MaxHeight = 240,
            SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle,
            ItemsSource = retrieved.RetrievedSearch.Citations,
            ItemTemplate = new FuncDataTemplate<LocalFileCitation>((citation, _) => new TextBlock
            {
                Text = citation is null ? string.Empty
                    : $"{citation.DisplayIdentity}, lines {citation.StartLine}-{citation.EndLine}\n{citation.Excerpt}",
                TextWrapping = TextWrapping.Wrap,
            }),
        };
        AutomationProperties.SetName(candidates, "Choose whole retrieved named-source excerpts; none selected by default");
        var chosen = Button("Review only chosen named-source excerpts", () =>
        {
            if (!current() || !ReferenceEquals(retrievedAnswer, retrieved))
            { throw new InvalidOperationException("The named-source retrieval was revoked."); }
            var selected = retrieved.SelectEvidence(candidates.SelectedItems!.Cast<LocalFileCitation>().ToArray());
            ShowAnswerReview(selected, current);
            return Task.CompletedTask;
        });
        chosen.IsEnabled = false;
        candidates.SelectionChanged += (_, _) =>
        {
            if (!current() || !ReferenceEquals(retrievedAnswer, retrieved)) { return; }
            answerCancellation?.Cancel();
            retrieved.InvalidateSelection();
            answerReview = null;
            answerLinks.Children.Clear();
            preview.Text = null;
            answerStatus.Text = "Selection changed. Review the chosen whole excerpts, then confirm separately. No consent is renewed.";
            chosen.IsEnabled = candidates.SelectedItems?.Count > 0;
        };
        answerChoices.Children.Add(new TextBlock
        {
            Text = "Optional: choose only relevant whole excerpts below, then review that set. None is selected by default. "
                + "The existing confirmation uses all shown excerpts unless you change this selection. Highlighting text is not context selection.",
            TextWrapping = TextWrapping.Wrap,
        });
        answerChoices.Children.Add(candidates);
        answerChoices.Children.Add(chosen);
    }

    private void ShowAnswerReview(KnowledgeSourceAnswerReview exact, Func<bool> current)
    {
        answerReview = exact;
        answerLinks.Children.Clear();
        answerStatus.Text = $"REVIEW: {exact.Source.Name}; exact retained revision {exact.Revision.Reference.RevisionId:D}\n"
            + $"Unchanged new question:\n{exact.Question}\n"
            + $"{exact.Search.Citations.Count} citations / {exact.ExcerptUtf8Bytes} excerpt bytes; "
            + $"limits 8 citations / 16 KiB excerpts / 32 KiB complete serialized input. "
            + $"User omitted {exact.RetrievedSearch.Citations.Count - exact.Search.Citations.Count} retrieved excerpts. "
            + $"Matching chunks: {exact.RetrievedSearch.MatchingChunks}; omitted matches: {exact.RetrievedSearch.Truncated}.\n"
            + string.Join("\n\n", exact.Search.Citations.Select(item =>
                $"{item.DisplayIdentity}, lines {item.StartLine}-{item.EndLine}\n{item.Excerpt}"))
            + "\nPrivate LocalOnly, no tools, hosted fallback or other sources. Files may contain secrets; risk screening is not a secret-free guarantee.\n"
            + LocalContextSessionTarget.FreshSessionDisclosure
            + $"\nSeparate confirmation permits ONE use of only this question and these excerpts; expires {exact.ExpiresAt:O}.";
        answerLinks.Children.Add(Button("Confirm one private local-only no-tools named-source answer", async () =>
        {
            if (!current() || !ReferenceEquals(answerReview, exact))
            { throw new InvalidOperationException("The exact named-source review was revoked."); }
            answerChoices.Children.Clear();
            answerLinks.Children.Clear();
            using var execution = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            answerCancellation = execution;
            try
            {
                var result = await answers!.AnswerAsync(exact, current, execution.Token);
                if (!current() || exact.IsRevoked || execution.IsCancellationRequested
                    || !ReferenceEquals(answerReview, exact)) { return; }
                answerStatus.Text = $"Named-source local answer: {result.Outcome}. "
                    + "Retained here only while this exact private result remains open; no conversation body or speech.\n"
                    + string.Join("\n\n", result.Claims.Select(claim => claim.Text));
                foreach (var citation in result.Claims.SelectMany(claim => claim.Citations).Distinct())
                {
                    answerLinks.Children.Add(Button($"Inspect exact answer citation: {citation.DisplayIdentity}, line {citation.StartLine}", () =>
                    {
                        if (!current() || exact.IsRevoked || !ReferenceEquals(answerReview, exact))
                        { throw new InvalidOperationException("The retained answer citation is unavailable."); }
                        var span = exact.Revision.ResolveCitation(citation);
                        preview.Text = span.File.Text;
                        preview.SelectionStart = citation.Start;
                        preview.SelectionEnd = citation.Start + citation.Length;
                        preview.CaretIndex = citation.Start;
                        preview.Focus();
                        return Task.CompletedTask;
                    }));
                }
            }
            finally { answerCancellation = null; }
        }));
    }

    private void RetireAnswer()
    {
        Interlocked.Increment(ref answerEpoch);
        answerCancellation?.Cancel();
        retrievedAnswer?.Revoke();
        retrievedAnswer = null;
        answerReview = null;
        answerStatus.Text = null;
        answerChoices.Children.Clear();
        answerLinks.Children.Clear();
    }
}
