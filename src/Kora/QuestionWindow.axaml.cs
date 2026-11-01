using System.ComponentModel;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Kora.Core.Interaction;
using Kora.Core.Presentation;

namespace Kora;

public sealed partial class QuestionWindow : Window
{
    private readonly NativeQuestionViewModel state;
    private readonly NativeDetailRenderer renderer;
    private readonly TextBox input;
    private readonly TextBox review;
    private readonly TextBox explanation;
    private readonly Dictionary<string, ToggleButton> choices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> fieldTexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, RadioButton>> fieldChoices = new(StringComparer.Ordinal);
    private readonly List<Button> fieldVoice = [];
    private readonly DispatcherTimer expiry = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool refreshing;
    private int foregroundAdmission;
    private TaskCompletionSource<bool>? focusAcknowledgement;
    private Func<bool>? focusAdmission;
    private long focusLifetime;

    public QuestionWindow() => throw new InvalidOperationException("A trusted host question is required.");

    internal QuestionWindow(NativeQuestionViewModel state, NativeDetailRenderer renderer)
    {
        this.state = state;
        this.renderer = renderer;
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty) { Interlocked.Increment(ref focusLifetime); }
        };
        AvaloniaXamlLoader.Load(this);
        input = Required<TextBox>("AnswerInput");
        review = Required<TextBox>("ExactReview");
        explanation = Required<TextBox>("PolicyExplanation");
        var record = state.Question;
        Required<TextBlock>("PromptLabel").Text = record.Spec.Text;
        Required<TextBlock>("TargetLabel").Text =
            $"Session: {record.Key.Request.SessionId.Value:D}\nTask: {record.Key.Request.TaskId.Value:D}"
            + $"\nOriginal origin: {record.Key.Request.Origin} - generation: {record.SessionGeneration.Value}"
            + $"\nQuestion: {record.Key.QuestionId.Value:D} - expires: {record.ExpiresAt:O}"
            + $"\nPurpose/source: {record.Spec.Purpose}/{record.Spec.SourceId}"
            + $"\nBounds: {record.Spec.Minimum}..{record.Spec.Maximum} choices; text maximum {record.Spec.MaximumTextLength}.";
        var panel = Required<StackPanel>("ChoicePanel");
        foreach (var field in record.Spec.Fields)
        {
            panel.Children.Add(new TextBlock
            {
                Text = field.Label + (field.Kind == QuestionKind.Text
                    ? $" (required text; maximum {field.MaximumLength} characters / {field.MaximumUtf8Bytes} UTF-8 bytes)"
                    : " (required; choose one)"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
            if (field.Kind == QuestionKind.Text)
            {
                var text = new TextBox
                {
                    Name = field.Id, AcceptsReturn = true, IsUndoEnabled = false,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinHeight = 42, MaxHeight = 120,
                };
                AutomationProperties.SetName(text, field.Label + " required text");
                text.PropertyChanged += (_, change) =>
                {
                    if (!refreshing && change.Property == TextBox.TextProperty)
                    { state.EditField(new(field.Id, text.Text ?? string.Empty)); }
                };
                fieldTexts.Add(field.Id, text);
                panel.Children.Add(text);
            }
            else
            {
                var controls = new Dictionary<string, RadioButton>(StringComparer.Ordinal);
                fieldChoices.Add(field.Id, controls);
                foreach (var option in field.Options)
                {
                    var radio = new RadioButton
                    {
                        Name = field.Id + "-" + option.Id,
                        Content = new TextBlock { Text = option.Label, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        GroupName = record.Key.QuestionId.Value.ToString("D") + "-" + field.Id,
                    };
                    AutomationProperties.SetName(radio, field.Label + ": " + option.Label);
                    radio.IsCheckedChanged += (_, _) =>
                    {
                        if (!refreshing && radio.IsChecked == true) { state.EditField(new(field.Id, Choice: option.Id)); }
                    };
                    controls.Add(option.Id, radio);
                    panel.Children.Add(radio);
                }
            }
            var speak = new Button { Content = "Speak field: " + field.Label, Name = "Speak-" + field.Id };
            speak.Click += async (_, _) => await RunInteractionAsync(() => state.AnswerFieldByVoiceAsync(field.Id));
            fieldVoice.Add(speak);
            panel.Children.Add(speak);
        }
        for (var index = 0; index < record.Spec.Options.Length; index++)
        {
            var option = record.Spec.Options[index];
            ToggleButton control = record.Spec.Kind == QuestionKind.SingleChoice
                ? new RadioButton { GroupName = record.Key.QuestionId.Value.ToString("D") }
                : new CheckBox();
            control.Content = option.Label;
            control.TabIndex = index;
            AutomationProperties.SetName(control, option.Label);
            control.IsCheckedChanged += (_, _) => Edit();
            choices.Add(option.Id, control);
            panel.Children.Add(control);
        }
        input.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty) { Edit(); }
        };
        input.IsVisible = record.Spec.Kind == QuestionKind.Text;
        Required<Button>("ReviewQuestion").Click += async (_, _) => await RunInteractionAsync(state.ReviewAsync);
        Required<Button>("ExplainPolicy").Click += async (_, _) => await RunInteractionAsync(state.ExplainPolicyAsync);
        Required<Button>("ReturnToApproval").Click += async (_, _) => await RunInteractionAsync(state.ReturnToApprovalAsync);
        Required<Button>("SaveDraft").Click += async (_, _) => await RunInteractionAsync(state.SaveDraftAsync);
        Required<Button>("SaveDraft").IsVisible = !state.IsForm;
        Required<Button>("SubmitAnswer").Content = state.IsApproval ? "_Approve exact operation" : "_Submit answer";
        Required<Button>("SubmitAnswer").Click += async (_, _) => await RunInteractionAsync(state.SubmitAsync);
        Required<Button>("CancelQuestion").Click += async (_, _) => await RunInteractionAsync(state.CancelAsync);
        Required<Button>("CloseQuestion").Click += (_, _) => Close();
        Required<Button>("AnswerByVoice").Click += async (_, _) => await RunInteractionAsync(() => state.AnswerByVoiceAsync());
        Required<Button>("HearThenAnswer").Click += async (_, _) => await RunInteractionAsync(() => state.AnswerByVoiceAsync(afterPlayback: true));
        Required<Button>("CancelReply").Click += (_, _) => state.StopVoiceReply();
        if (state.ClipboardAnswer is { } clipboard)
        {
            clipboard.BindWindow(this);
            Required<Button>("UseClipboardAnswer").Click += async (_, _) => await RunInteractionAsync(clipboard.CaptureAsync);
            Required<Button>("AcceptClipboardDraft").Click += async (_, _) => await RunInteractionAsync(clipboard.AcceptAsync);
            Required<Button>("CancelClipboardAnswer").Click += (_, _) => clipboard.Clear();
            Required<CheckBox>("ReplaceManualDraft").IsCheckedChanged += (_, _) =>
            {
                if (!refreshing) { clipboard.AcknowledgeReplacement(Required<CheckBox>("ReplaceManualDraft").IsChecked == true); }
            };
        }
        var clipboardReview = Required<TextBox>("ClipboardAnswerReview");
        clipboardReview.CopyingToClipboard += BlockClipboard;
        clipboardReview.CuttingToClipboard += BlockClipboard;
        clipboardReview.PastingFromClipboard += BlockClipboard;
        clipboardReview.AddHandler(ContextRequestedEvent, BlockClipboard, RoutingStrategies.Tunnel);
        state.BindVisibleVoicePresenter(() => Volatile.Read(ref foregroundAdmission) == 1);
        Deactivated += (_, _) =>
        {
            Interlocked.Exchange(ref foregroundAdmission, 0);
            state.StopVoiceReply();
        };
        review.CopyingToClipboard += BlockClipboard;
        review.CuttingToClipboard += BlockClipboard;
        review.PastingFromClipboard += BlockClipboard;
        review.AddHandler(ContextRequestedEvent, BlockClipboard, RoutingStrategies.Tunnel);
        explanation.CopyingToClipboard += BlockClipboard;
        explanation.CuttingToClipboard += BlockClipboard;
        explanation.PastingFromClipboard += BlockClipboard;
        explanation.AddHandler(ContextRequestedEvent, BlockClipboard, RoutingStrategies.Tunnel);
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; Close(); }
        };
        state.PropertyChanged += OnStateChanged;
        Activated += async (_, _) =>
        {
            Interlocked.Exchange(ref foregroundAdmission, 1);
            if (focusAcknowledgement is { } acknowledgement)
            {
                var admitted = focusAdmission;
                focusAcknowledgement = null;
                focusAdmission = null;
                try
                {
                    acknowledgement.TrySetResult(admitted?.Invoke() == true && IsVisible && IsActive
                        && state.HasPresentation && state.IsEditable);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    acknowledgement.TrySetException(exception);
                }
                return;
            }
            await RunInteractionAsync(state.RefreshTargetAsync);
            await state.TryConversationalReplyAsync();
        };
        expiry.Tick += OnExpiry;
        Opened += (_, _) =>
        {
            expiry.Start();
            FocusAnswer();
        };
        Closed += (_, _) =>
        {
            focusAcknowledgement?.TrySetResult(false);
            focusAcknowledgement = null;
            focusAdmission = null;
            Interlocked.Exchange(ref foregroundAdmission, 0);
            expiry.Stop();
            expiry.Tick -= OnExpiry;
            state.PropertyChanged -= OnStateChanged;
            state.Dismiss();
            refreshing = true;
            input.Text = string.Empty;
            review.Text = string.Empty;
            clipboardReview.Text = string.Empty;
            explanation.Text = string.Empty;
            panel.Children.Clear();
            choices.Clear();
            foreach (var text in fieldTexts.Values) { text.Text = string.Empty; }
            fieldTexts.Clear();
            fieldChoices.Clear();
            fieldVoice.Clear();
            Required<TextBlock>("PromptLabel").Text = string.Empty;
            Required<TextBlock>("TargetLabel").Text = string.Empty;
            Required<TextBlock>("QuestionStatus").Text = string.Empty;
        };
        Refresh();
    }

    internal void ReportOutcome(string message) => state.ReportOutcome(message);

    internal Func<bool> CaptureFocusLifetime()
    {
        var revision = Interlocked.Read(ref focusLifetime);
        return () => IsVisible && revision == Interlocked.Read(ref focusLifetime);
    }

    internal async Task<bool> FocusExistingAsync(Func<bool> admitted)
    {
        if (!admitted() || focusAcknowledgement is not null || !IsVisible
            || !state.HasPresentation || !state.IsEditable) { return false; }
        if (IsActive) { return admitted(); }
        var acknowledgement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        focusAdmission = admitted;
        focusAcknowledgement = acknowledgement;
        try
        {
            Activate();
            return await acknowledgement.Task.WaitAsync(TimeSpan.FromSeconds(2))
                && admitted() && IsVisible && IsActive && state.HasPresentation && state.IsEditable;
        }
        // Keep a timed-out activation marked focus-only until its event or closure, never refresh its source.
        catch (TimeoutException) { return false; }
    }

    private async Task RunInteractionAsync(Func<Task> interaction)
    {
        var focused = FocusManager?.GetFocusedElement() as Control;
        await interaction();
        if (!IsVisible || !IsActive || !state.HasPresentation
            || (FocusManager?.GetFocusedElement() is { } current && !ReferenceEquals(current, this)))
        {
            return;
        }
        if (focused is not null && ReferenceEquals(TopLevel.GetTopLevel(focused), this) && focused.IsEffectivelyEnabled)
        {
            focused.Focus();
            return;
        }
        if (state.IsEditable)
        {
            FocusAnswer();
        }
        else { Required<Button>("CloseQuestion").Focus(); }
    }

    private T Required<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException("A native question control is missing.");

    private void FocusAnswer()
    {
        if (fieldTexts.Count != 0) { fieldTexts.Values.First().Focus(); }
        else if (fieldChoices.Count != 0) { fieldChoices.Values.First().Values.First().Focus(); }
        else if (choices.Count != 0) { choices.Values.First().Focus(); }
        else { input.Focus(); }
    }

    private void Edit()
    {
        if (refreshing) { return; }
        state.Edit(state.Question.Spec.Kind == QuestionKind.Text
            ? new([], input.Text ?? string.Empty)
            : new(choices.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key)));
    }

    private void OnExpiry(object? sender, EventArgs args)
    {
        state.ClipboardAnswer?.Refresh();
        state.RefreshEligibility();
    }
    private void OnStateChanged(object? sender, PropertyChangedEventArgs args) => Refresh();

    private void Refresh()
    {
        refreshing = true;
        if (!state.HasPresentation)
        {
            Required<TextBlock>("PromptLabel").Text = string.Empty;
            Required<TextBlock>("TargetLabel").Text = string.Empty;
            Required<StackPanel>("ChoicePanel").Children.Clear();
            choices.Clear();
            foreach (var text in fieldTexts.Values) { text.Text = string.Empty; }
            fieldTexts.Clear();
            fieldChoices.Clear();
            fieldVoice.Clear();
        }
        foreach (var (id, control) in choices)
        {
            control.IsChecked = state.Answer.Choices.Contains(id, StringComparer.Ordinal);
            control.IsEnabled = state.IsEditable;
        }
        if (!string.Equals(input.Text, state.Answer.Text, StringComparison.Ordinal)) { input.Text = state.Answer.Text; }
        input.IsEnabled = state.IsEditable;
        var answer = state.Answer;
        foreach (var (id, text) in fieldTexts)
        {
            var value = answer.Fields.SingleOrDefault(field => string.Equals(field.FieldId, id, StringComparison.Ordinal))?.Text ?? string.Empty;
            if (!string.Equals(text.Text, value, StringComparison.Ordinal)) { text.Text = value; }
            text.IsEnabled = state.IsEditable;
        }
        foreach (var (id, controls) in fieldChoices)
        {
            var value = answer.Fields.SingleOrDefault(field => string.Equals(field.FieldId, id, StringComparison.Ordinal))?.Choice;
            foreach (var (choice, radio) in controls)
            {
                radio.IsChecked = string.Equals(choice, value, StringComparison.Ordinal);
                radio.IsEnabled = state.IsEditable;
            }
        }
        foreach (var button in fieldVoice) { button.IsEnabled = state.CanEditFieldByVoice; }
        Required<Button>("ReviewQuestion").IsEnabled = state.IsEditable;
        Required<Button>("ExplainPolicy").IsVisible = state.SupportsPolicyExplanation;
        Required<Button>("ExplainPolicy").IsEnabled = state.IsEditable && !state.HasPolicyExplanation;
        Required<Button>("ReturnToApproval").IsVisible = state.HasPolicyExplanation;
        Required<Button>("ReturnToApproval").IsEnabled = state.IsEditable;
        explanation.IsVisible = state.HasPolicyExplanation;
        explanation.Text = state.PolicyExplanationText;
        Required<Button>("SaveDraft").IsEnabled = state.CanSaveDraft;
        Required<Button>("SubmitAnswer").IsEnabled = state.CanSubmit;
        Required<Button>("CancelQuestion").IsEnabled = state.IsEditable;
        Required<Button>("AnswerByVoice").IsEnabled = state.CanAnswerByVoice;
        Required<Button>("HearThenAnswer").IsEnabled = state.CanAnswerByVoice;
        Required<Button>("CancelReply").IsEnabled = state.IsVoiceReplyActive;
        var clipboard = state.ClipboardAnswer;
        var clipboardText = clipboard?.ReviewText ?? string.Empty;
        var clipboardReview = Required<TextBox>("ClipboardAnswerReview");
        clipboardReview.Text = clipboardText;
        clipboardReview.IsVisible = clipboardText.Length != 0;
        review.IsVisible = clipboardText.Length == 0;
        if (clipboardText.Length != 0) { clipboard!.CompletePresentation(clipboardReview.Text ?? string.Empty); }
        Required<Button>("UseClipboardAnswer").IsVisible = clipboard is not null;
        Required<Button>("UseClipboardAnswer").IsEnabled = clipboard?.CanCapture == true;
        Required<Button>("AcceptClipboardDraft").IsVisible = clipboardText.Length != 0 && clipboard?.HasAccepted == false;
        Required<Button>("AcceptClipboardDraft").IsEnabled = clipboard?.CanAccept == true;
        Required<Button>("CancelClipboardAnswer").IsVisible = clipboard?.IsActive == true;
        var replacement = Required<CheckBox>("ReplaceManualDraft");
        replacement.IsVisible = clipboardText.Length != 0 && clipboard?.NeedsReplacement == true && !clipboard.HasAccepted;
        if (!replacement.IsVisible) { replacement.IsChecked = false; }
        Required<TextBlock>("VoiceReplyStatus").Text = state.VoiceReplyStatus;
        Required<TextBlock>("QuestionStatus").Text = $"Revision {state.Key.Revision.Value}: {state.Status}";
        if (!string.Equals(review.Text, state.ReviewText, StringComparison.Ordinal))
        {
            review.SelectionStart = 0;
            review.SelectionEnd = 0;
            review.Text = state.ReviewText.Length == 0 ? string.Empty
                : renderer.Render(state.ReviewText, DetailContentKind.PlainText).SemanticText;
            if (state.ReviewText.Length != 0) { state.CompleteReviewPresentation(review.Text ?? string.Empty); }
        }
        refreshing = false;
    }

    private static void BlockClipboard(object? sender, RoutedEventArgs args) => args.Handled = true;
}
