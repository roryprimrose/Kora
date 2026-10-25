using System.ComponentModel;
using System.Text;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

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
    private SessionDetailView? activeDetails;
    private SessionsViewModel? observedModel;
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

    public SessionInteractionView()
    {
        InitializeComponent();
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
        DiscardMessage.Click += async (_, _) => await Run(model => model.DiscardComposerBufferAsync());
        SessionList.SelectionChanged += async (_, args) =>
        {
            if (args.AddedItems.Count == 1 && args.AddedItems[0] is SessionWorkspaceEntry entry)
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
        HideSessionDetails.Click += (_, _) => { HideDetails(); ShowSessionDetails.Focus(); };
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
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Avalonia routed Click requires void; failures are caught and reported through logged model recovery.")]
    private async void OnDetailCommandClicked(object? sender, RoutedEventArgs args)
    {
        try
        {
            if (args.Source is Button { Name: "DeclineSessionDetailsOffer", DataContext: ConversationMessage message }
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

    internal async Task ShowAvailableDetailsAsync(SessionsViewModel model, bool acceptOffer = false)
    {
        if (!ReferenceEquals(model, observedModel) || !model.CanShowDetails) { return; }
        if (acceptOffer && model.DetailsOfferMessage is { } offered)
        { await model.OpenConversationDetailAsync(offered, assistant: true); return; }
        if (model.LastDetailMessage is { } previous)
        { await model.OpenConversationDetailAsync(previous, model.LastDetailAssistant); return; }
        if (model.LastEvidenceDetail is { } evidence)
        { await model.OpenEvidenceDetailsAsync(evidence); return; }
        if (model.CanRestoreHistoryDetail)
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
