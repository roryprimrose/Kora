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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora;

internal sealed partial class QuestionView : UserControl, INativeQuestionPresentation, IDisposable
{
    private readonly Window owner;
    private readonly NativeViewLifetime nativeLifetime;
    private bool disposed;
    private bool withdrawn;
    private bool wasPresented;
    private bool activating;
    private bool focusOnlyActivation;
    private bool handledActivation;
    private bool reviewRequested;
    private bool nativeReviewPresented;
    private bool clipboardReviewPresented;
    private bool explanationPresented;
    private string lastClipboardReview = string.Empty;
    private string lastExplanation = string.Empty;
    public event EventHandler? Closed;
    public bool IsPresented => nativeLifetime.IsPresented && !Volatile.Read(ref withdrawn) && !Volatile.Read(ref disposed);
    public bool IsActive => nativeLifetime.IsActive && IsPresented;
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal Task InteractionOperation { get; private set; } = Task.CompletedTask;
    internal NativeQuestionViewModel State => state;
    private readonly ILogger logger;
    private readonly NativeQuestionViewModel state;
    private readonly NativeDetailRenderer renderer;
    private readonly TextBox input;
    private readonly TextBox review;
    private readonly TextBox explanation;
    private readonly bool mcpArgumentInput;
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

    public QuestionView() => throw new InvalidOperationException("A trusted host question and owning view are required.");

    internal QuestionView(NativeQuestionViewModel state, NativeDetailRenderer renderer, Window owner, ILogger? logger = null)
    {
        this.state = state;
        this.renderer = renderer;
        this.owner = owner;
        this.logger = logger ?? NullLogger.Instance;
        AvaloniaXamlLoader.Load(this);
        nativeLifetime = new(this, owner, () => !disposed);
        nativeLifetime.PresentationChanged += OnPresentationChanged;
        nativeLifetime.ActivationChanged += OnActivationChanged;
        owner.Closed += OnOwnerClosed;
        owner.Activated += OnOwnerActivated;
        input = Required<TextBox>("AnswerInput");
        review = Required<TextBox>("ExactReview");
        explanation = Required<TextBox>("PolicyExplanation");
        var record = state.Question;
        var selected = owner is ResponseWindow primary
            ? (primary.SessionView.DataContext as SessionsViewModel)?.SelectedSessionRecord : null;
        Required<TextBlock>("SourceSummary").Text = selected?.Authority.SessionId == record.Key.Request.SessionId
            ? "Original question for " + (selected.Metadata?.Name.Value ?? "this conversation")
            : "Original host question - not the selected conversation";
        Required<Button>("ReopenQuestion").Click += (_, _) => Activate();
        SizeChanged += (_, _) => Required<Grid>("QuestionLayout").Height = Math.Max(420, Bounds.Height - 24);
        mcpArgumentInput = record.Spec.Kind == QuestionKind.Text
            && string.Equals(record.Spec.SourceId, "mcp-tool-arguments", StringComparison.Ordinal);
        reviewRequested = mcpArgumentInput;
        Required<TextBlock>("PromptLabel").Text = mcpArgumentInput
            ? "Enter JSON arguments matching the complete schema below. Submitting input is NOT approval to send it."
            : record.Spec.Text;
        if (mcpArgumentInput)
        {
            input.MaxHeight = 96;
            AutomationProperties.SetName(review, "Complete immutable MCP argument schema or exact record - input is not approval");
        }
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
        Required<Button>("ReviewQuestion").Click += async (_, _) =>
        {
            reviewRequested = true;
            await RunInteractionAsync(state.ReviewAsync);
        };
        Required<Button>("ExplainPolicy").Click += async (_, _) => await RunInteractionAsync(state.ExplainPolicyAsync);
        Required<Button>("ReturnToApproval").Click += async (_, _) =>
        {
            reviewRequested = true;
            await RunInteractionAsync(state.ReturnToApprovalAsync);
        };
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
            clipboard.BindWindow(owner, CaptureFocusLifetime);
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
        expiry.Tick += OnExpiry;
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

    public void ReportOutcome(string message) => state.ReportOutcome(message);

    public Func<bool> CaptureFocusLifetime()
    {
        var revision = Interlocked.Read(ref focusLifetime);
        var admitted = IsPresented;
        var native = nativeLifetime.Capture();
        return () => admitted && native() && revision == Interlocked.Read(ref focusLifetime);
    }

    public async Task<bool> FocusExistingAsync(Func<bool> admitted)
    {
        if (!admitted() || focusAcknowledgement is not null || !IsPresented
            || !state.HasPresentation || !state.IsEditable) { return false; }
        focusOnlyActivation = true;
        if (IsActive) { return admitted(); }
        var acknowledgement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        focusAdmission = admitted;
        focusAcknowledgement = acknowledgement;
        try
        {
            owner.Activate();
            return await acknowledgement.Task.WaitAsync(TimeSpan.FromSeconds(2))
                && admitted() && IsPresented && IsActive && state.HasPresentation && state.IsEditable;
        }
        // Keep a timed-out activation marked focus-only until its event or closure, never refresh its source.
        catch (TimeoutException) { return false; }
    }

    private Task RunInteractionAsync(Func<Task> interaction) =>
        InteractionOperation = RunInteractionCoreAsync(interaction);

    private async Task RunInteractionCoreAsync(Func<Task> interaction)
    {
        if (!IsPresented)
        {
            state.ReportOutcome("Question controls are not presented. Return to this exact original question; no answer or approval was accepted.");
            return;
        }
        var focused = owner.FocusManager?.GetFocusedElement() as Control;
        await interaction();
        if (!IsPresented || !IsActive || !state.HasPresentation
            || (owner.FocusManager?.GetFocusedElement() is { } current
                && !ReferenceEquals(current, this) && !ReferenceEquals(current, owner)))
        {
            return;
        }
        if (focused is not null && ReferenceEquals(TopLevel.GetTopLevel(focused), owner) && focused.IsEffectivelyEnabled)
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
        if (withdrawn || disposed) { return; }
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
        var explanationText = state.PolicyExplanationText;
        if (owner is ResponseWindow explanationOwner)
        {
            if (!string.Equals(lastExplanation, explanationText, StringComparison.Ordinal))
            {
                lastExplanation = explanationText;
                if (explanationText.Length == 0) { explanationOwner.RemoveNativeReview(explanation); }
                else if (IsPresented)
                {
                    explanation.Text = explanationText;
                    explanationOwner.ShowNativeReview(explanation, "Original pending action policy - not approval",
                        explanation, () => IsPresented && state.HasPolicyExplanation, OnExplanationPresented);
                }
            }
            explanation.Text = explanationPresented ? explanationText : string.Empty;
        }
        else { explanation.Text = explanationText; }
        Required<Button>("SaveDraft").IsEnabled = state.CanSaveDraft;
        Required<Button>("SubmitAnswer").IsEnabled = state.CanSubmit;
        Required<Button>("CancelQuestion").IsEnabled = state.IsEditable;
        Required<Button>("AnswerByVoice").IsEnabled = state.CanAnswerByVoice;
        Required<Button>("HearThenAnswer").IsEnabled = state.CanAnswerByVoice;
        Required<Button>("CancelReply").IsEnabled = state.IsVoiceReplyActive;
        var clipboard = state.ClipboardAnswer;
        var clipboardText = clipboard?.ReviewText ?? string.Empty;
        var clipboardReview = Required<TextBox>("ClipboardAnswerReview");
        if (owner is ResponseWindow clipboardOwner)
        {
            if (!string.Equals(lastClipboardReview, clipboardText, StringComparison.Ordinal))
            {
                lastClipboardReview = clipboardText;
                if (clipboardText.Length == 0) { clipboardOwner.RemoveNativeReview(clipboardReview); }
                else if (IsPresented)
                {
                    clipboardReview.Text = clipboardText;
                    clipboardOwner.ShowNativeReview(clipboardReview, "Original clipboard answer snapshot - unsubmitted",
                        clipboardReview, () => IsPresented && state.ClipboardAnswer?.IsActive == true,
                        OnClipboardReviewPresented);
                }
            }
            clipboardReview.Text = clipboardReviewPresented ? clipboardText : string.Empty;
        }
        else { clipboardReview.Text = clipboardText; }
        clipboardReview.IsVisible = clipboardText.Length != 0;
        review.IsVisible = clipboardText.Length == 0;
        if (clipboardText.Length != 0 && (owner is not ResponseWindow || clipboardReviewPresented))
        { clipboard!.CompletePresentation(clipboardReview.Text ?? string.Empty); }
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
        var visibleReview = mcpArgumentInput && state.HasPresentation && state.ReviewText.Length == 0
            ? state.Question.Spec.Text : state.ReviewText;
        if (owner is ResponseWindow primary)
        {
            if (reviewRequested && visibleReview.Length != 0 && IsPresented)
            {
                reviewRequested = false;
                SetReviewText(visibleReview);
                primary.ShowNativeReview(this, mcpArgumentInput && state.ReviewText.Length == 0
                    ? "MCP argument schema - input is not approval" : "Original question or operation review",
                    review, () => IsPresented && state.HasPresentation, OnNativeReviewPresented);
            }
            if (nativeReviewPresented)
            {
                SetReviewText(visibleReview);
                if (state.ReviewText.Length != 0) { state.CompleteReviewPresentation(review.Text ?? string.Empty); }
            }
            else { review.Text = string.Empty; }
        }
        else if (!string.Equals(review.Text, visibleReview, StringComparison.Ordinal))
        {
            SetReviewText(visibleReview);
            if (state.ReviewText.Length != 0) { state.CompleteReviewPresentation(review.Text ?? string.Empty); }
        }
        refreshing = false;
    }

    private void SetReviewText(string text)
    {
        if (string.Equals(review.Text, text, StringComparison.Ordinal)) { return; }
        review.SelectionStart = 0;
        review.SelectionEnd = 0;
        review.Text = text.Length == 0 ? string.Empty : renderer.Render(text, DetailContentKind.PlainText).SemanticText;
    }

    private void OnNativeReviewPresented(bool presented)
    {
        if (nativeReviewPresented == presented) { return; }
        nativeReviewPresented = presented;
        if (!presented)
        {
            review.Text = string.Empty;
            state.RetireReviewPresentation();
        }
        else { Refresh(); }
    }

    private void OnClipboardReviewPresented(bool presented)
    {
        if (clipboardReviewPresented == presented) { return; }
        clipboardReviewPresented = presented;
        if (!presented)
        {
            Required<TextBox>("ClipboardAnswerReview").Text = string.Empty;
            state.ClipboardAnswer?.CompletePresentation(string.Empty);
            state.ClipboardChanged();
        }
        else { Refresh(); }
    }

    private void OnExplanationPresented(bool presented)
    {
        if (explanationPresented == presented) { return; }
        explanationPresented = presented;
        if (!presented) { explanation.Text = string.Empty; }
        else { Refresh(); }
    }

    private static void BlockClipboard(object? sender, RoutedEventArgs args) => args.Handled = true;

    public void Activate()
    {
        if (disposed || !nativeLifetime.IsPresented) { return; }
        var reveal = withdrawn;
        if (reveal) { handledActivation = false; }
        focusOnlyActivation = false;
        state.RefreshEligibility();
        if (!state.HasPresentation || !state.IsEditable) { Refresh(); return; }
        withdrawn = false;
        Required<ScrollViewer>("QuestionCard").IsVisible = true;
        Required<Button>("ReopenQuestion").IsVisible = false;
        Refresh();
        expiry.Start();
        owner.Activate();
        if (reveal && IsActive) { StartActivation(); }
    }

    public void Close() => Dispose();

    private void OnPresentationChanged(object? sender, EventArgs args)
    {
        Interlocked.Increment(ref focusLifetime);
        if (!nativeLifetime.IsPresented)
        {
            if (!wasPresented || disposed) { return; }
            withdrawn = true;
            expiry.Stop();
            Interlocked.Exchange(ref foregroundAdmission, 0);
            state.StopVoiceReply();
            state.ClipboardAnswer?.Clear();
            state.RetireReviewPresentation();
            Required<ScrollViewer>("QuestionCard").IsVisible = false;
            Required<Button>("ReopenQuestion").IsVisible = true;
            refreshing = true;
            input.Text = string.Empty;
            review.Text = string.Empty;
            explanation.Text = string.Empty;
            Required<TextBox>("ClipboardAnswerReview").Text = string.Empty;
            foreach (var text in fieldTexts.Values) { text.Text = string.Empty; }
            refreshing = false;
            return;
        }
        if (wasPresented) { return; }
        wasPresented = true;
        expiry.Start();
        FocusAnswer();
        Refresh();
    }

    private void OnActivationChanged(object? sender, EventArgs args)
    {
        if (!IsActive)
        {
            handledActivation = false;
            if (focusAcknowledgement is null) { focusOnlyActivation = false; }
            Interlocked.Exchange(ref foregroundAdmission, 0);
            state.StopVoiceReply();
        }
        else if (focusOnlyActivation)
        {
            Interlocked.Exchange(ref foregroundAdmission, 1);
            CompleteFocusAcknowledgement();
        }
        else { StartActivation(); }
    }

    private void StartActivation()
    {
        if (activating || handledActivation || !IsPresented) { return; }
        handledActivation = true;
        Operation = ActivateAsync();
    }

    private void OnOwnerActivated(object? sender, EventArgs args)
    {
        if (!focusOnlyActivation) { StartActivation(); }
    }

    private async Task ActivateAsync()
    {
        activating = true;
        try
        {
            Interlocked.Exchange(ref foregroundAdmission, 1);
            if (focusAcknowledgement is not null)
            {
                CompleteFocusAcknowledgement();
                return;
            }
            await RunInteractionAsync(state.RefreshTargetAsync);
            if (IsPresented && IsActive) { await state.TryConversationalReplyAsync(); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            state.StopVoiceReply();
            ActivationFailed(exception.GetType().Name);
            state.ReportOutcome("Native question activation failed. No answer or approval is claimed. Failure type: " + exception.GetType().Name);
        }
        finally { activating = false; }
    }

    private void CompleteFocusAcknowledgement()
    {
        if (focusAcknowledgement is not { } acknowledgement) { return; }
        var admitted = focusAdmission;
        focusAcknowledgement = null;
        focusAdmission = null;
        acknowledgement.TrySetResult(admitted?.Invoke() == true && IsPresented && IsActive
            && state.HasPresentation && state.IsEditable);
    }

    private void OnOwnerClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        if (owner is ResponseWindow primary)
        {
            primary.RemoveNativeReview(this);
            primary.RemoveNativeReview(explanation);
            primary.RemoveNativeReview(Required<TextBox>("ClipboardAnswerReview"));
        }
        nativeLifetime.PresentationChanged -= OnPresentationChanged;
        nativeLifetime.ActivationChanged -= OnActivationChanged;
        nativeLifetime.Dispose();
        owner.Closed -= OnOwnerClosed;
        owner.Activated -= OnOwnerActivated;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
