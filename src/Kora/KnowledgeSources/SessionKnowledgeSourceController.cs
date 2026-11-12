using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using Kora.Application.KnowledgeSources;
using Kora.Application.ViewModels;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Core.Storage;

namespace Kora.KnowledgeSources;

internal sealed class SessionKnowledgeSourceController(MainViewModel host, Func<Window?> owner,
    Func<SessionKnowledgeSourceWorkflow> createWorkflow)
{
    private Action? close;
    internal void Close() => close?.Invoke();

    internal async Task OpenAsync(SessionWorkspaceEntry target, Func<bool> admitted, CancellationToken token)
    {
        if (close is not null) { throw new InvalidOperationException("Close the current source review before opening another."); }
        var parent = owner() ?? throw new InvalidOperationException("The selected conversation's native owner is unavailable.");
        await using var workflow = createWorkflow();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var native = host.CaptureKnowledgeSourceAnswerEligibility(RequestOrigin.LocalUi);
        bool Current() => !lifetime.IsCancellationRequested && admitted() && native();
        var window = new Window { Title = "This conversation's documentation source", Width = 820, Height = 760 };
        var body = new StackPanel { Spacing = 8, Margin = new(16) };
        window.Content = new ScrollViewer { Content = body };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var detail = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180 };
        var choices = new ComboBox
        {
            PlaceholderText = "Deliberately choose one existing enabled source",
            ItemTemplate = new FuncDataTemplate<KnowledgeSource>((item, _) => new TextBlock { Text = item?.Name }),
        };
        var question = new TextBox { PlaceholderText = "One new question; opening a source is not model consent" };
        AutomationProperties.SetName(choices, "Existing registered source for this exact conversation");
        AutomationProperties.SetName(question, "New question for this conversation's associated source");
        AutomationProperties.SetName(status, "Conversation source review and exact outcome");
        AutomationProperties.SetName(detail, "Explicitly opened source or complete question and excerpt review");
        var citations = new StackPanel { Spacing = 4 };
        SessionKnowledgeSourceBinding? binding = null;
        SessionKnowledgeSourceWorkflow.BindingReview? bindingReview = null;
        SessionKnowledgeSourceBinding? detachReview = null;
        SessionKnowledgeSourceWorkflow.OpenedSource? opened = null;
        KnowledgeSourceAnswerReview? answerReview = null;
        CancellationTokenSource? executing = null;
        Task operation = Task.CompletedTask;
        Task inputRetirement = Task.CompletedTask;
        long inputEpoch = 0;
        void RetireInput(bool cancel = true)
        {
            inputEpoch++;
            if (cancel && executing is { } active)
            { inputRetirement = Task.WhenAll(inputRetirement, active.CancelAsync()); }
            bindingReview = null;
            detachReview = null;
            answerReview?.Revoke();
            answerReview = null;
            citations.Children.Clear();
            detail.Text = null;
        }
        var stopping = false;
        void Stop()
        {
            if (stopping) { return; }
            stopping = true;
            RetireInput(cancel: false);
            opened = null;
            stopped.TrySetResult();
            window.Close();
        }
        close = Stop;
        window.Closed += (_, _) => Stop();
        choices.SelectionChanged += (_, _) => RetireInput();
        void PrivacyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (!Current()) { Stop(); }
        }
        void PrivacyClosed(object? sender, EventArgs args) => Stop();
        host.PropertyChanged += PrivacyChanged;
        host.PrivacyClosureRequested += PrivacyClosed;
        using var cancellation = token.Register(() => Dispatcher.UIThread.Post(Stop));
        async Task Execute(Func<CancellationToken, Task> action)
        {
            if (executing is not null)
            { host.ReportHostInteractionFailure("Finish or cancel the current exact source operation before another review."); return; }
            using var execution = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            executing = execution;
            using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Request);
            try
            {
                if (!Current()) { throw new InvalidOperationException("Original private conversation admission changed."); }
                choices.IsEnabled = false;
                await action(execution.Token);
                if (!Current()) { throw new OperationCanceledException("Source presentation retired."); }
                activity.Complete(HostOperationOutcome.Completed);
            }
            catch (OperationCanceledException)
            {
                detail.Text = null;
                citations.Children.Clear();
                status.Text = "Cancelled or retired. No rollback or retained answer is inferred; inspect the original conversation before retrying.";
                activity.Complete(HostOperationOutcome.Cancelled);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                detail.Text = null;
                citations.Children.Clear();
                status.Text = "Source operation unavailable. Inspect durable state before retrying. Failure type: " + exception.GetType().Name;
                host.ReportHostInteractionFailure(status.Text);
                activity.Complete(HostOperationOutcome.Failed);
            }
            finally { executing = null; choices.IsEnabled = true; }
        }
        Button Action(string label, Func<CancellationToken, Task> action)
        {
            var button = new Button { Content = label };
            AutomationProperties.SetName(button, label);
            button.Command = new Kora.Application.Infrastructure.AsyncCommand(
                () => operation = Execute(action),
                exception => host.ReportHostInteractionFailure("Source command failed. Failure type: " + exception.GetType().Name));
            return button;
        }
        body.Children.Add(new TextBlock
        {
            Text = "Exact selected conversation: " + (target.Metadata?.Name.Value ?? "Unnamed conversation")
                + "\n" + SessionKnowledgeSourceWorkflow.BindingReview.Disclosure,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(choices);
        body.Children.Add(Action("Open selected existing revision and review association", async cancellationToken =>
        {
            if (binding is not null) { throw new InvalidOperationException("Review and confirm detach before selecting another association."); }
            if (choices.SelectedItem is not KnowledgeSource { State: KnowledgeSourceState.Enabled } source)
            { throw new InvalidOperationException("Explicitly select one existing enabled source."); }
            RetireInput(cancel: false);
            bindingReview = await workflow.ReviewBindingAsync(target, source, Current, cancellationToken);
            status.Text = "Review association to " + source.Name + ". No model invoked. Confirm separately.";
            detail.Text = $"Source: {source.Name}\nOriginal root: {source.Folder.CanonicalPath}\n"
                + $"Traversal: {source.Folder.Scope}; {source.Folder.Files.Count} retained files; {source.StoredBytes} stored bytes.\n"
                + $"Registered revision last changed: {source.ChangedAt:O}\n\n"
                + string.Join("\n", source.Folder.Files.Select(file => file.CanonicalPath))
                + "\n\n" + SessionKnowledgeSourceWorkflow.BindingReview.Disclosure;
        }));
        body.Children.Add(Action("Confirm this exact metadata-only association", async cancellationToken =>
        {
            var exact = bindingReview ?? throw new InvalidOperationException("Review an exact source association first.");
            bindingReview = null;
            binding = await workflow.ConfirmBindingAsync(exact, Current, cancellationToken);
            status.Text = "Exact association committed with audited readback. Source use is not authorized. Open it deliberately below.";
            detail.Text = null;
        }));
        body.Children.Add(Action("Explicitly open associated source with current access and integrity checks", async cancellationToken =>
        {
            var exact = binding ?? throw new InvalidOperationException("No source association exists.");
            RetireInput(cancel: false);
            opened = await workflow.OpenSourceAsync(target, exact, Current, cancellationToken);
            detail.Text = string.Join("\n\n", opened.Revision.Files.Select(file => file.Text));
            status.Text = "Exact associated revision opened. No question submitted, model invoked or retention renewed.";
        }));
        body.Children.Add(question);
        question.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty)
            {
                RetireInput();
                status.Text = "Question changed. Review the unchanged question and exact excerpts, then confirm one use separately.";
            }
        };
        body.Children.Add(Action("Review question, exact excerpts and original conversation target", async cancellationToken =>
        {
            var source = opened ?? throw new InvalidOperationException("Explicitly open the associated source first.");
            var epoch = inputEpoch;
            bool QuestionCurrent() => Current() && epoch == inputEpoch && ReferenceEquals(opened, source);
            var prepared = await workflow.PrepareQuestionAsync(source, question.Text ?? string.Empty, QuestionCurrent, cancellationToken);
            answerReview = prepared.Review;
            if (prepared.Review is not { } exact)
            { status.Text = $"Question review: {prepared.Outcome}. No model invoked."; return; }
            detail.Text = $"Unchanged question:\n{exact.Question}\n\nOriginal selected conversation; no new conversation.\n"
                + $"{exact.Search.Citations.Count} citations; {exact.ExcerptUtf8Bytes} excerpt bytes; "
                + $"omitted matching sections: {exact.Search.MatchingChunks - exact.Search.Citations.Count}.\n\n"
                + string.Join("\n\n", exact.Search.Citations.Select(citation => exact.DescribeCitation(citation) + "\n" + citation.Excerpt))
                + "\n\nOne private LocalOnly/toolsNONE use, no hosted fallback. Files may contain secrets; screening is not a secret-free guarantee. "
                + "Retain the unchanged question and validated cited answer in this original conversation under session-history retention. "
                + "Source retention remains independent. Separate confirmation required before " + exact.ExpiresAt.ToString("O");
            status.Text = "Question, exact excerpts, privacy and original target reviewed. No model invoked.";
        }));
        body.Children.Add(Action("Confirm one LocalOnly toolsNONE answer retained in this original conversation", async cancellationToken =>
        {
            var exact = answerReview ?? throw new InvalidOperationException("Review the current question and excerpts first.");
            var source = opened;
            var epoch = inputEpoch;
            bool QuestionCurrent() => Current() && epoch == inputEpoch && ReferenceEquals(opened, source);
            var result = await workflow.AnswerAsync(exact, QuestionCurrent, cancellationToken);
            if (!QuestionCurrent()) { throw new OperationCanceledException("Question or target changed."); }
            status.Text = $"Original-conversation source answer: {result.Outcome}. "
                + (result.Outcome is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.Unsupported
                    ? "The exact question and validated final answer are retained in the original conversation. No speech."
                    : "No retained supported answer is claimed. Inspect the original conversation before retrying.");
            detail.Text = string.Join("\n\n", result.Claims.Select(claim => claim.Text));
            citations.Children.Clear();
            foreach (var citation in result.Claims.SelectMany(claim => claim.Citations).Distinct())
            {
                citations.Children.Add(Action("Inspect retained citation: " + exact.DescribeCitation(citation), cancellationToken =>
                {
                    if (!QuestionCurrent() || exact.IsRevoked || !ReferenceEquals(answerReview, exact))
                    { throw new InvalidOperationException("The exact source citation was retired."); }
                    cancellationToken.ThrowIfCancellationRequested();
                    var span = exact.ResolveCitation(citation);
                    detail.Text = span.File.Text;
                    detail.SelectionStart = citation.Start;
                    detail.SelectionEnd = citation.Start + citation.Length;
                    detail.CaretIndex = citation.Start;
                    detail.Focus();
                    return Task.CompletedTask;
                }));
            }
        }));
        body.Children.Add(Action("Review detach association only", async cancellationToken =>
        {
            RetireInput(cancel: false);
            await workflow.RevokeAsync();
            opened = null;
            detachReview = binding ?? throw new InvalidOperationException("No association exists.");
            cancellationToken.ThrowIfCancellationRequested();
            status.Text = "Detach review: remove only this exact association. Source bytes, source retention and conversation history remain unchanged. Confirm separately.";
        }));
        body.Children.Add(Action("Confirm detach this exact association only", async cancellationToken =>
        {
            var exact = detachReview ?? throw new InvalidOperationException("Review the exact detach operation first.");
            detachReview = null;
            await workflow.DetachAsync(target, exact, Current, cancellationToken);
            binding = null;
            status.Text = "Exact association detached with audited readback. Independent source and conversation data are unchanged.";
        }));
        body.Children.Add(status);
        body.Children.Add(detail);
        body.Children.Add(citations);
        body.Children.Add(Action("Close source review", _ => { Stop(); return Task.CompletedTask; }));
        try
        {
            binding = await workflow.ReadBindingAsync(target, token);
            choices.ItemsSource = await workflow.ListSourcesAsync(token);
            choices.SelectedIndex = -1;
            if (!Current()) { throw new InvalidOperationException("Private source opening admission expired."); }
            status.Text = binding is null ? "No association. Select one existing enabled revision deliberately."
                : "Association metadata reopened. No source body has been read and no source/model authority renewed.";
            window.Show(parent);
            window.Activate();
#pragma warning disable VSTHRD003 // Joins only this window's lifetime and exact owned native operation.
            await stopped.Task;
            await lifetime.CancelAsync();
            await inputRetirement;
            await workflow.RevokeAsync();
            await operation;
#pragma warning restore VSTHRD003
        }
        finally
        {
            host.PropertyChanged -= PrivacyChanged;
            host.PrivacyClosureRequested -= PrivacyClosed;
            close = null;
            await lifetime.CancelAsync();
            window.Close();
            window.Content = null;
        }
    }
}
