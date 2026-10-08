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
    private readonly Dictionary<string, ToggleButton> choices = new(StringComparer.Ordinal);
    private readonly DispatcherTimer expiry = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool refreshing;

    public QuestionWindow() => throw new InvalidOperationException("A trusted host question is required.");

    internal QuestionWindow(NativeQuestionViewModel state, NativeDetailRenderer renderer)
    {
        this.state = state;
        this.renderer = renderer;
        AvaloniaXamlLoader.Load(this);
        input = Required<TextBox>("AnswerInput");
        review = Required<TextBox>("ExactReview");
        var record = state.Question;
        Required<TextBlock>("PromptLabel").Text = record.Spec.Text;
        Required<TextBlock>("TargetLabel").Text =
            $"Session: {record.Key.Request.SessionId.Value:D}\nTask: {record.Key.Request.TaskId.Value:D}"
            + $"\nOriginal origin: {record.Key.Request.Origin} - generation: {record.SessionGeneration.Value}"
            + $"\nQuestion: {record.Key.QuestionId.Value:D} - expires: {record.ExpiresAt:O}"
            + $"\nPurpose/source: {record.Spec.Purpose}/{record.Spec.SourceId}"
            + $"\nBounds: {record.Spec.Minimum}..{record.Spec.Maximum} choices; text maximum {record.Spec.MaximumTextLength}.";
        var panel = Required<StackPanel>("ChoicePanel");
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
        input.TextChanged += (_, _) => Edit();
        input.IsVisible = record.Spec.Kind == QuestionKind.Text;
        Required<Button>("ReviewQuestion").Click += async (_, _) => await RunInteractionAsync(state.ReviewAsync);
        Required<Button>("SaveDraft").Click += async (_, _) => await RunInteractionAsync(state.SaveDraftAsync);
        Required<Button>("SubmitAnswer").Content = state.IsApproval ? "_Approve exact operation" : "_Submit answer";
        Required<Button>("SubmitAnswer").Click += async (_, _) => await RunInteractionAsync(state.SubmitAsync);
        Required<Button>("CancelQuestion").Click += async (_, _) => await RunInteractionAsync(state.CancelAsync);
        Required<Button>("CloseQuestion").Click += (_, _) => Close();
        review.CopyingToClipboard += BlockClipboard;
        review.CuttingToClipboard += BlockClipboard;
        review.PastingFromClipboard += BlockClipboard;
        review.AddHandler(ContextRequestedEvent, BlockClipboard, RoutingStrategies.Tunnel);
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; Close(); }
        };
        state.PropertyChanged += OnStateChanged;
        Activated += async (_, _) => await RunInteractionAsync(state.RefreshTargetAsync);
        expiry.Tick += OnExpiry;
        Opened += (_, _) =>
        {
            expiry.Start();
            if (choices.Count != 0) { choices.Values.First().Focus(); }
            else { input.Focus(); }
        };
        Closed += (_, _) =>
        {
            expiry.Stop();
            expiry.Tick -= OnExpiry;
            state.PropertyChanged -= OnStateChanged;
            state.Close("Native presentation closed. No answer, approval or execution is implied by window closure.");
            refreshing = true;
            input.Text = string.Empty;
            review.Text = string.Empty;
            panel.Children.Clear();
            choices.Clear();
            Required<TextBlock>("PromptLabel").Text = string.Empty;
            Required<TextBlock>("TargetLabel").Text = string.Empty;
            Required<TextBlock>("QuestionStatus").Text = string.Empty;
        };
        Refresh();
    }

    internal void ReportOutcome(string message) => state.ReportOutcome(message);

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
            if (choices.Count != 0) { choices.Values.First().Focus(); }
            else { input.Focus(); }
        }
        else { Required<Button>("CloseQuestion").Focus(); }
    }

    private T Required<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException("A native question control is missing.");

    private void Edit()
    {
        if (refreshing) { return; }
        state.Edit(state.Question.Spec.Kind == QuestionKind.Text
            ? new([], input.Text ?? string.Empty)
            : new(choices.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key)));
    }

    private void OnExpiry(object? sender, EventArgs args) => state.RefreshEligibility();
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
        }
        foreach (var (id, control) in choices)
        {
            control.IsChecked = state.Answer.Choices.Contains(id, StringComparer.Ordinal);
            control.IsEnabled = state.IsEditable;
        }
        if (!string.Equals(input.Text, state.Answer.Text, StringComparison.Ordinal)) { input.Text = state.Answer.Text; }
        input.IsEnabled = state.IsEditable;
        Required<Button>("ReviewQuestion").IsEnabled = state.IsEditable;
        Required<Button>("SaveDraft").IsEnabled = state.CanSaveDraft;
        Required<Button>("SubmitAnswer").IsEnabled = state.CanSubmit;
        Required<Button>("CancelQuestion").IsEnabled = state.IsEditable;
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
