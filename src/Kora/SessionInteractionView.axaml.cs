using System.ComponentModel;
using System.Text;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Kora.Core.Presentation;
using Kora.Core.Diagnostics;
using Kora.Application.Diagnostics;
using Kora.Controls;
using Kora.Core.Conversation;
using Avalonia.Interactivity;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionInteractionView : UserControl
{
    internal event EventHandler? ManagementRequested;
    internal event EventHandler? NewConversationRequested;
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal Task CancellationOperation { get; private set; } = Task.CompletedTask;
    private SessionDetailView? activeDetails;
    private SessionsViewModel? observedModel;
    private readonly ReplyPresentationFence replyPresentation;
    private Guid? selectedSession;
    private readonly Dictionary<DetailContentReference, DetailReadingPosition> readingPositions = [];
    private GridLength conversationWidth = new(1, GridUnitType.Star);
    private GridLength detailWidth = new(1, GridUnitType.Star);
    private NamedTextBlock? evidenceBody;
    private Func<bool>? evidenceAdmission;
    private Func<CancellationToken, Task<bool>>? validateEvidence;
    private CancellationTokenSource? evidenceLifetime;
    private readonly DispatcherTimer evidenceValidation = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool validatingEvidence;
    private readonly EventHandler<RoutedEventArgs> detailCommandClick;
    internal Task EvidenceValidationOperation { get; private set; } = Task.CompletedTask;
    internal bool DetailsVisible => DetailsPane.IsVisible;
    internal bool IsConversationPresented => IsVisible && ConversationTabs.SelectedItem == ConversationTab;
    private long conversationPresentationRevision;
    internal long ConversationPresentationRevision => Interlocked.Read(ref conversationPresentationRevision);

    public SessionInteractionView()
    {
        InitializeComponent();
        replyPresentation = new(this);
        ConversationTabs.SelectionChanged += (_, _) =>
        { Interlocked.Increment(ref conversationPresentationRevision); replyPresentation.Retire(); };
        RefreshSessions.Click += async (_, _) => await Run(model => model.RefreshAsync());
        NextSessions.Click += async (_, _) => await Run(model => model.NextAsync());
        SearchSessionNames.Click += async (_, _) => await Run(model => model.SearchSessionNamesAsync());
        NextSessionNameSearch.Click += async (_, _) => await Run(model => model.SearchSessionNamesAsync(next: true));
        ClearSessionNameSearch.Click += (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { model.ClearListSearch(); SessionNameQuery.Focus(); }
        };
        CancelSessionNameSearch.Click += (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { model.CancelListSearch(); }
        };
        SessionNameQuery.KeyDown += async (_, args) =>
        {
            if (DataContext is not SessionsViewModel model) { return; }
            if (args.Key == Key.Enter && model.CanSearchSessionNames)
            {
                args.Handled = true;
                await Run(current => current.SearchSessionNamesAsync());
            }
            else if (args.Key == Key.Escape)
            {
                args.Handled = true;
                model.ClearListSearch();
            }
        };
        ReadConversation.Click += async (_, _) => await Run(model => model.ReadConversationAsync());
        NextConversation.Click += async (_, _) => await Run(model => model.ReadConversationAsync(next: true));
        ReadSessionLogs.Click += async (_, _) => await Run(model => model.ReadEvidenceDetailsAsync(EvidenceSource.Log));
        ReadSessionAudit.Click += async (_, _) => await Run(model => model.ReadEvidenceDetailsAsync(EvidenceSource.AuthorityAudit));
        ReadActivity.Click += async (_, _) => await Run(model => model.ReadHistoryAsync());
        NextActivity.Click += async (_, _) => await Run(model => model.ReadHistoryAsync(next: true));
        ActivityDetails.Click += async (_, _) => await Run(model => model.OpenHistoryDetailAsync());
        SubmitMessage.Click += async (_, _) => await Run(model => model.SubmitConversationAsync());
        SubmitLocalAnswer.Click += async (_, _) => await Run(model => model.SubmitLocalAnswerAsync());
        StopLocalAnswer.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model)
            { await (CancellationOperation = model.CancelRunningConversationAsync()); }
        };
        DiscardMessage.Click += async (_, _) => await Run(model => model.DiscardComposerBufferAsync());
        SessionList.SelectionChanged += async (_, args) =>
        {
            if (args.AddedItems.Count == 1 && args.AddedItems[0] is SessionWorkspaceEntry entry
                && DataContext is SessionsViewModel selectedModel && selectedModel.SelectedSessionRecord != entry)
            { await Run(model => model.SelectAsync(entry)); }
        };
        ActivityList.SelectionChanged += (_, args) =>
        {
            if (DataContext is SessionsViewModel model && args.AddedItems.Count == 1
                && args.AddedItems[0] is SessionHistoryEvent entry)
            { model.SelectHistoryRecord(entry); }
        };
        ManageSessions.Click += (_, _) => ManagementRequested?.Invoke(this, EventArgs.Empty);
        NewConversation.Click += (_, _) => NewConversationRequested?.Invoke(this, EventArgs.Empty);
        ShowSessionDetails.Click += async (_, _) => await Run(model => ShowAvailableDetailsAsync(model));
        BackToConversation.Click += (_, _) =>
        {
            if (!FocusConversationOrigin(activeDetails?.Content))
            {
                RefreshReturnNavigation();
                DetailNavigationStatus.Text = "Return unavailable: the exact originating message is not currently accessible. "
                    + "Select its session and explicitly read the conversation; no replacement was chosen.";
            }
        };
        HideSessionDetails.Click += (_, _) => HideDetailsAndRestoreFocus();
        evidenceValidation.Tick += async (_, _) => await (EvidenceValidationOperation = RevalidateEvidenceAsync());
        DetachedFromVisualTree += (_, _) => HideDetails();
        DataContextChanged += (_, _) => ObserveModel();
#pragma warning disable MA0147 // Avalonia routed-event boundary; failures use logged model recovery.
        detailCommandClick = OnDetailCommandClicked;
#pragma warning restore MA0147
        AddHandler(Button.ClickEvent, detailCommandClick);
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible) { HideDetails(); }
            if (args.Property == IsVisibleProperty) { Interlocked.Increment(ref conversationPresentationRevision); }
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Avalonia routed Click requires void; failures are caught and reported through logged model recovery.")]
    private async void OnDetailCommandClicked(object? sender, RoutedEventArgs args)
    {
        try
        {
            if (args.Source is Button { Name: "OpenSubmittedAnswerContext", DataContext: ConversationMessage answer })
            { args.Handled = true; await Run(current => current.OpenConversationDetailAsync(answer, submittedContext: true)); }
            else if (args.Source is Button { Name: "ReplyToAssistantAnswer", DataContext: ConversationMessage replySource }
                && DataContext is SessionsViewModel)
            {
                args.Handled = true;
                var presenter = replyPresentation.Capture();
                await Run(current => current.ReplyToAnswerAsync(replySource, presenter));
            }
            else if (args.Source is Button { Name: "OpenObservedResult", DataContext: ObservationDetailChoice observation })
            { args.Handled = true; await Run(current => current.OpenObservationAsync(observation)); }
            else if (args.Source is Button { Name: "DeclineSessionDetailsOffer", DataContext: ConversationMessage message }
                && DataContext is SessionsViewModel model)
            { args.Handled = true; model.DismissDetailsOffer(message); }
            else if (args.Source is Button { Name: "OpenSessionEvidence", DataContext: SessionEvidenceDetail detail })
            { args.Handled = true; await Run(current => current.OpenEvidenceDetailsAsync(detail)); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { HideDetails(); observedModel?.ReportDetailPresentationFailure(exception); }
    }

    private void ObserveModel()
    {
        if (observedModel is not null) { observedModel.PropertyChanged -= OnModelChanged; }
        HideDetails();
        observedModel = DataContext as SessionsViewModel;
        selectedSession = observedModel?.SelectedSessionRecord?.Authority.SessionId.Value;
        if (observedModel is not null) { observedModel.PropertyChanged += OnModelChanged; }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        var current = observedModel?.SelectedSessionRecord?.Authority.SessionId.Value;
        if (current != selectedSession || observedModel?.CanPresentDetails == false || evidenceAdmission?.Invoke() == false)
        {
            selectedSession = current;
            HideDetails();
            if (observedModel?.CanPresentDetails == false) { readingPositions.Clear(); }
        }
        RefreshReturnNavigation();
    }

    private ConversationMessage? ConversationOrigin(AdmittedDetailContent? content)
    {
        if (!IsVisible || observedModel?.CanPresentDetails != true
            || content?.Origin is not (DetailContentOrigin.RetainedConversation or DetailContentOrigin.RetainedAssistant
                or DetailContentOrigin.RetainedLocalObservation or DetailContentOrigin.RetainedAnswerContext)
            || content.ConversationMessage is not { } message
            || observedModel.SelectedSessionRecord?.Authority.SessionId != message.Request.SessionId)
        { return null; }
        return observedModel.ConversationMessages.FirstOrDefault(current => current == message);
    }

    private void RefreshReturnNavigation()
    {
        BackToConversation.IsEnabled = DetailsVisible && ConversationOrigin(activeDetails?.Content) is not null;
        DetailNavigationStatus.Text = BackToConversation.IsEnabled
            ? "Back to conversation focuses the exact originating message and leaves Details open."
            : "Return unavailable: this view has no message on the currently observed conversation page. "
                + "Hide details returns to Show details.";
    }

    private bool FocusConversationOrigin(AdmittedDetailContent? content)
    {
        if (content is null || ConversationOrigin(content) is not { } message) { return false; }
        ConversationTabs.SelectedItem = ConversationTab;
        UpdateLayout();
        var name = content.Origin == DetailContentOrigin.RetainedAnswerContext ? "OpenSubmittedAnswerContext"
            : content.Origin == DetailContentOrigin.RetainedLocalObservation ? "OpenObservedResult"
            : content.Origin == DetailContentOrigin.RetainedAssistant
            ? "OpenAssistantConversationDetail" : "OpenConversationDetail";
        var origin = ConversationTabs.GetVisualDescendants().OfType<Button>().FirstOrDefault(button =>
            string.Equals(button.Name, name, StringComparison.Ordinal)
            && (content.Origin == DetailContentOrigin.RetainedLocalObservation
                ? button.DataContext is ObservationDetailChoice choice && choice.Message == message && choice.Observation.Id == content.Reference.ItemId.Value
                : Equals(button.DataContext, message))
            && button.IsEffectivelyVisible && button.IsEnabled);
        if (origin is null || ConversationOrigin(content) != message) { return false; }
        origin.BringIntoView();
        return ConversationOrigin(content) == message && origin.Focus();
    }

    internal bool ShowDetails(SessionDetailView view, AdmittedDetailContent content)
    {
        var subject = content.ConversationMessage?.Request.SessionId ?? content.HistorySession ?? content.SessionSource?.SessionId;
        var selected = observedModel?.SelectedSessionRecord?.Authority.SessionId;
        if (subject is null || observedModel?.CanPresentDetails != true
            || content.Origin != DetailContentOrigin.FinalizedResponse && selected != subject)
        { return false; }
        if (TopLevel.GetTopLevel(this) is not ResponseWindow window || !window.PrepareDetailsLayout()) { return false; }
        if (!ReferenceEquals(activeDetails, view)) { HideDetails(); }
        activeDetails = view;
        DetailsHeading.Text = content.Origin == DetailContentOrigin.FinalizedResponse
            ? "Current response details - not the selected conversation" : "Session details";
        DetailsContent.Content = view.Reader;
        RevealDetails();
        if (readingPositions.TryGetValue(content.Reference, out var position))
        { view.Reader.RestoreReadingPosition(position); }
        return true;
    }

    internal void RememberReadingPosition(DetailContentReference reference, DetailReadingPosition position)
    {
        if (observedModel?.CanPresentDetails != true) { return; }
        if (!readingPositions.ContainsKey(reference) && readingPositions.Count == 8) { readingPositions.Remove(readingPositions.Keys.First()); }
        readingPositions[reference] = position;
    }

    private void RevealDetails()
    {
        ReadingPanes.ColumnDefinitions[0].Width = conversationWidth;
        ReadingPanes.ColumnDefinitions[1].Width = new(6);
        ReadingPanes.ColumnDefinitions[2].Width = detailWidth;
        DetailsDivider.IsVisible = true;
        DetailsPane.IsVisible = true;
        ShowSessionDetails.IsVisible = false;
        RefreshReturnNavigation();
    }

    private void RememberSplit()
    {
        if (!DetailsVisible) { return; }
        conversationWidth = ReadingPanes.ColumnDefinitions[0].Width;
        detailWidth = ReadingPanes.ColumnDefinitions[2].Width;
    }

    internal void RemoveDetails(SessionDetailView view)
    {
        if (!ReferenceEquals(activeDetails, view)) { return; }
        RememberSplit();
        activeDetails = null;
        DetailsContent.Content = null;
        DetailsPane.IsVisible = false;
        DetailsDivider.IsVisible = false;
        ReadingPanes.ColumnDefinitions[1].Width = new(0);
        ReadingPanes.ColumnDefinitions[2].Width = new(0);
        ShowSessionDetails.IsVisible = true;
        RefreshReturnNavigation();
        if (IsVisible && observedModel?.CanPresentDetails == true) { ShowSessionDetails.Focus(); }
    }

    internal void HideDetails()
    {
        RememberSplit();
        evidenceValidation.Stop();
        var retired = evidenceLifetime;
        evidenceLifetime = null;
        validateEvidence = null;
        retired?.Cancel();
        if (!validatingEvidence) { retired?.Dispose(); }
        activeDetails?.ClearAndClose();
        if (evidenceBody is not null) { evidenceBody.Text = null; evidenceBody = null; }
        evidenceAdmission = null;
        if (DetailsContent.Content is Control chooser) { DetailsContent.Content = null; chooser.DataContext = null; }
        DetailsPane.IsVisible = false;
        DetailsDivider.IsVisible = false;
        ReadingPanes.ColumnDefinitions[1].Width = new(0);
        ReadingPanes.ColumnDefinitions[2].Width = new(0);
        ShowSessionDetails.IsVisible = true;
        RefreshReturnNavigation();
    }

    internal void HideDetailsAndRestoreFocus()
    {
        var origin = activeDetails?.Content;
        HideDetails();
        if (!FocusConversationOrigin(origin) && IsVisible && observedModel?.CanPresentDetails == true)
        { ShowSessionDetails.Focus(); }
    }

    internal bool ShowEvidence(SessionEvidenceDetail detail, Func<bool> admitted, Func<CancellationToken, Task<bool>> validate)
    {
        if (!admitted() || observedModel?.CanPresentDetails != true
            || TopLevel.GetTopLevel(this) is not ResponseWindow window || !window.PrepareDetailsLayout())
        { return false; }
        HideDetails();
        var body = new NamedTextBlock
        {
            Text = Encoding.UTF8.GetString(DurableEvidenceQuery.Serialize(detail.Page)),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        evidenceBody = body;
        DetailsHeading.Text = detail.Source == EvidenceSource.AuthorityAudit
            ? "Session typed security audit" : "Session diagnostic evidence";
        evidenceAdmission = admitted;
        validateEvidence = validate;
        evidenceLifetime = new();
        DetailsContent.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        RevealDetails();
        if (!admitted()) { body.Text = null; HideDetails(); return false; }
        evidenceValidation.Start();
        return true;
    }

    internal async Task RevalidateEvidenceAsync()
    {
        if (validatingEvidence || evidenceLifetime is not { } lifetime || validateEvidence is not { } validate) { return; }
        validatingEvidence = true;
        try
        {
            if (!await validate(lifetime.Token) && ReferenceEquals(lifetime, evidenceLifetime)) { HideDetails(); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            observedModel?.ReportDetailPresentationFailure(exception);
            if (ReferenceEquals(lifetime, evidenceLifetime)) { HideDetails(); }
        }
        finally
        {
            validatingEvidence = false;
            if (!ReferenceEquals(lifetime, evidenceLifetime)) { lifetime.Dispose(); }
        }
    }

    internal async Task ShowAvailableDetailsAsync(SessionsViewModel model, bool acceptOffer = false, bool choose = false)
    {
        if (!ReferenceEquals(model, observedModel) || !model.CanShowDetails) { return; }
        if (!choose && acceptOffer && model.DetailsOfferMessage is { } offered)
        { await model.OpenConversationDetailAsync(offered, assistant: true); return; }
        if (!choose && model.LastDetailMessage is { } previous)
        { await model.OpenConversationDetailAsync(previous, model.LastDetailAssistant, model.LastDetailObservation,
            submittedContext: model.LastDetailContext); return; }
        if (!choose && model.LastEvidenceDetail is { } evidence)
        { await model.OpenEvidenceDetailsAsync(evidence); return; }
        if (!choose && model.CanRestoreHistoryDetail)
        { await model.OpenHistoryDetailAsync(); return; }
        if (TopLevel.GetTopLevel(this) is not ResponseWindow window || !window.PrepareDetailsLayout()) { return; }
        var choices = new StackPanel { Spacing = 8 };
        foreach (var message in model.ConversationMessages)
        {
            choices.Children.Add(new Button
            {
                Name = message.HasAssistantResponse ? "OpenAssistantConversationDetail" : "OpenConversationDetail", DataContext = message,
                Content = $"Message {message.Sequence}: {(message.HasAssistantResponse ? "answer and original message" : "original message")}",
            });
        }
        foreach (var choice in model.ObservationChoices)
        {
            choices.Children.Add(new Button { Name = "OpenObservedResult", DataContext = choice, Content = choice.Label });
        }
        if (model.CanOpenHistoryDetail)
        {
            var history = new Button { Content = "Selected activity details" };
            history.Click += async (_, _) => await Run(current => current.OpenHistoryDetailAsync());
            choices.Children.Add(history);
        }
        if (model.HasEvidenceDetails)
        {
            foreach (var detail in model.EvidenceDetails)
            {
                choices.Children.Add(new Button
                { Name = "OpenSessionEvidence", DataContext = detail, Content = detail.Label });
            }
        }
        HideDetails();
        DetailsContent.Content = choices;
        RevealDetails();
    }

    private Task Run(Func<SessionsViewModel, Task> operation) =>
        Operation = DataContext is SessionsViewModel model ? operation(model) : Task.CompletedTask;
}
