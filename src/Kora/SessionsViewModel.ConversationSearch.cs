using System.Globalization;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private string conversationQuery = string.Empty;
    private ConversationSearchRole conversationRole;
    private ConversationTerminalFilter conversationTerminal;
    private string conversationMinimumSequence = string.Empty;
    private string conversationMaximumSequence = string.Empty;
    private string conversationSubmittedFrom = string.Empty;
    private string conversationSubmittedThrough = string.Empty;
    private string conversationRequestId = string.Empty;
    private string conversationTaskId = string.Empty;
    private ConversationSearchPage? conversationSearchPage;
    private CancellationTokenSource? conversationSearchCancellation;
    private long conversationSearchEpoch;
    private bool conversationSearchBusy;
    private SessionWorkspaceEntry? conversationSearchTarget;
    private SessionWorkspaceEntry? conversationSearchDetailTarget;
    private long conversationSearchControlRevision;
    private string conversationSearchStatus = "Search only this exact session's retained USER messages and typed HOST outcomes. No model context or receipt-history search.";

    public string ConversationQuery
    {
        get => conversationQuery;
        set
        {
            if (closed || !access.CanInspect) { return; }
            InvalidateConversationSearch();
            conversationQuery = value;
            conversationSearchTarget = selected;
            conversationSearchControlRevision = access.ControlRevision;
            conversationSearchStatus = "Query edited. Explicit Search required; composer and saved draft are unchanged.";
            NotifyConversationSearch();
        }
    }
    public string ConversationSearchStatus => conversationSearchStatus;
    public string ConversationRequestId
    {
        get => conversationRequestId;
        set => EditConversationRange(ref conversationRequestId, value);
    }
    public string ConversationTaskId
    {
        get => conversationTaskId;
        set => EditConversationRange(ref conversationTaskId, value);
    }
    public string ConversationMinimumSequence
    {
        get => conversationMinimumSequence;
        set => EditConversationRange(ref conversationMinimumSequence, value);
    }
    public string ConversationMaximumSequence
    {
        get => conversationMaximumSequence;
        set => EditConversationRange(ref conversationMaximumSequence, value);
    }
    public string ConversationSubmittedFrom
    {
        get => conversationSubmittedFrom;
        set => EditConversationRange(ref conversationSubmittedFrom, value);
    }
    public string ConversationSubmittedThrough
    {
        get => conversationSubmittedThrough;
        set => EditConversationRange(ref conversationSubmittedThrough, value);
    }

    private void EditConversationRange(ref string field, string value)
    {
        if (closed || !access.CanInspect || string.Equals(field, value, StringComparison.Ordinal)) { return; }
        InvalidateConversationSearch();
        field = value;
        conversationSearchTarget = selected;
        conversationSearchControlRevision = access.ControlRevision;
        conversationSearchStatus = "Metadata predicate edited. Explicit Search required; empty means Any, not authority.";
        NotifyConversationSearch();
    }

    private void ResetConversationRanges()
    {
        conversationMinimumSequence = string.Empty;
        conversationMaximumSequence = string.Empty;
        conversationSubmittedFrom = string.Empty;
        conversationSubmittedThrough = string.Empty;
        conversationRequestId = string.Empty;
        conversationTaskId = string.Empty;
    }
    public IReadOnlyList<ConversationSearchRole> ConversationRoles { get; } = Enum.GetValues<ConversationSearchRole>();
    public ConversationSearchRole ConversationRole
    {
        get => conversationRole;
        set
        {
            if (closed || !access.CanInspect || conversationRole == value) { return; }
            InvalidateConversationSearch();
            conversationRole = value;
            conversationSearchTarget = selected;
            conversationSearchControlRevision = access.ControlRevision;
            conversationSearchStatus = "Role filter edited. Explicit Search required; composer and saved draft are unchanged.";
            NotifyConversationSearch();
        }
    }
    public IReadOnlyList<ConversationMessage> ConversationSearchMessages => conversationSearchPage?.Messages ?? [];
    public ModelTurnOutcome? ConversationOutcome
    {
        get => conversationTerminal.Outcome;
        set => EditConversationTerminal(conversationTerminal with { Outcome = value });
    }
    public ModelTurnReason? ConversationReason
    {
        get => conversationTerminal.Reason;
        set => EditConversationTerminal(conversationTerminal with { Reason = value });
    }
    public HostTaskState? ConversationTaskState
    {
        get => conversationTerminal.TaskState;
        set => EditConversationTerminal(conversationTerminal with { TaskState = value });
    }
    public IReadOnlyList<ConversationTerminalOption<ModelTurnOutcome>> ConversationOutcomes { get; } =
        [new(null), .. Enum.GetValues<ModelTurnOutcome>().Select(value => new ConversationTerminalOption<ModelTurnOutcome>(value))];
    public IReadOnlyList<ConversationTerminalOption<ModelTurnReason>> ConversationReasons { get; } =
        [new(null), .. Enum.GetValues<ModelTurnReason>().Select(value => new ConversationTerminalOption<ModelTurnReason>(value))];
    public IReadOnlyList<ConversationTerminalOption<HostTaskState>> ConversationTaskStates { get; } =
        [new(null), .. Enum.GetValues<HostTaskState>().Select(value => new ConversationTerminalOption<HostTaskState>(value))];
    public ConversationTerminalOption<ModelTurnOutcome> ConversationOutcomeChoice
    {
        get => ConversationOutcomes.FirstOrDefault(option => option.Value == ConversationOutcome) ?? new(ConversationOutcome);
        set { if (value is not null) { ConversationOutcome = value.Value; } }
    }
    public ConversationTerminalOption<ModelTurnReason> ConversationReasonChoice
    {
        get => ConversationReasons.FirstOrDefault(option => option.Value == ConversationReason) ?? new(ConversationReason);
        set { if (value is not null) { ConversationReason = value.Value; } }
    }
    public ConversationTerminalOption<HostTaskState> ConversationTaskStateChoice
    {
        get => ConversationTaskStates.FirstOrDefault(option => option.Value == ConversationTaskState) ?? new(ConversationTaskState);
        set { if (value is not null) { ConversationTaskState = value.Value; } }
    }

    private void EditConversationTerminal(ConversationTerminalFilter value)
    {
        if (closed || !access.CanInspect || conversationTerminal == value) { return; }
        InvalidateConversationSearch();
        conversationTerminal = value;
        conversationSearchTarget = selected;
        conversationSearchControlRevision = access.ControlRevision;
        conversationSearchStatus = "Terminal HOST filter edited. Explicit Search required; USER-only with a terminal predicate yields zero.";
        NotifyConversationSearch();
    }
    public bool CanSearchConversation => CanReadConversation && !conversationSearchBusy;
    public bool CanNextConversationSearch => CanSearchConversation && conversationSearchPage?.Next is not null;
    public bool CanCancelConversationSearch => conversationSearchBusy;

    public void ClearConversationSearch()
    {
        InvalidateConversationSearch();
        conversationQuery = string.Empty;
        conversationRole = ConversationSearchRole.All;
        conversationTerminal = default;
        ResetConversationRanges();
        conversationSearchTarget = null;
        conversationSearchStatus = "Private search query, rows and continuation cleared. Composer and saved draft unchanged.";
        NotifyConversationSearch();
    }

    private void InvalidateConversationSearch()
    {
        conversationSearchEpoch++;
        conversationSearchCancellation?.Cancel();
        conversationSearchPage = null;
        if (conversationSearchDetailTarget is { } detailTarget)
        {
            conversationSearchDetailTarget = null;
            revokeConversationDetails?.Invoke(detailTarget.Authority.SessionId);
        }
    }

    public async Task SearchConversationAsync(bool next = false)
    {
        if (!CanSearchConversation) { return; }
        var target = RequireSelected();
        var selection = selectionEpoch;
        var revision = access.ControlRevision;
        var query = conversationQuery;
        var role = conversationRole;
        var terminal = conversationTerminal;
        var minimumSequence = conversationMinimumSequence;
        var maximumSequence = conversationMaximumSequence;
        var submittedFrom = conversationSubmittedFrom;
        var submittedThrough = conversationSubmittedThrough;
        var requestId = conversationRequestId;
        var taskId = conversationTaskId;
        conversationSearchTarget = target;
        conversationSearchControlRevision = revision;
        var cursor = next ? conversationSearchPage?.Next : null;
        InvalidateConversationSearch();
        var epoch = conversationSearchEpoch;
        bool Live() => !closed && access.CanInspect && access.ControlRevision == revision
            && selectionEpoch == selection && selected == target && conversationSearchEpoch == epoch;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        conversationSearchCancellation = cancellation;
        conversationSearchBusy = true;
        conversationSearchStatus = "Reading one bounded exact conversation snapshot; no save, submission or model context.";
        NotifyConversationSearch();
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), Kora.Core.Hosting.RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        try
        {
            if (next && cursor is null) { throw new ArgumentException("CursorMismatch", nameof(next)); }
            ConversationRangeFilter range;
            try { range = ConversationRangeFilter.Parse(minimumSequence, maximumSequence, submittedFrom, submittedThrough); }
            catch (InvalidDataException exception)
            {
                conversationSearchStatus = "InvalidRange. Use ordered inclusive sequence bounds 1-"
                    + ConversationText.MaximumSessionMessages.ToString(CultureInfo.InvariantCulture)
                    + " and canonical UTC yyyy-MM-ddTHH:mm:ss.fffffff+00:00; empty means Any. Correct the fields and explicitly Search.";
                Failure(logger, exception.GetType().Name);
                activity.Complete(HostOperationOutcome.Failed);
                return;
            }
            ConversationCorrelationFilter correlation;
            try { correlation = ConversationCorrelationFilter.Parse(requestId, taskId); }
            catch (InvalidDataException exception)
            {
                conversationSearchStatus = "InvalidCorrelation. Use complete nonempty canonical lowercase D-format request/task GUIDs; empty means Any. No whitespace or repair. Correct the fields and explicitly Search.";
                Failure(logger, exception.GetType().Name);
                activity.Complete(HostOperationOutcome.Failed);
                return;
            }
            var result = await conversation!.SearchAsync(target.Authority.SessionId, query, cursor,
                ConversationSearchPage.DefaultRecords, cancellation.Token, role, terminal, range, correlation);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Live() || result.Session != target.Authority)
            { throw new OperationCanceledException("Exact private conversation search was revoked."); }
            conversationSearchPage = result;
            conversationSearchStatus = "Observed " + result.Messages.Length.ToString(CultureInfo.InvariantCulture)
                + " exact " + result.Role + " field matches; scanned " + result.Scanned.ToString(CultureInfo.InvariantCulture)
                + ", omitted 0, unavailable 0. "
                + (result.Next is null ? "End of original sequence snapshot." : "Next continues the original snapshot.")
                + " Chronological inert text; not generated answers, ranking or execution authority.";
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (Live())
            {
                conversationSearchPage = null;
                if (exception is not ArgumentException and not OperationCanceledException)
                {
                    InvalidateConversationSearch();
                    conversationQuery = string.Empty;
                    conversationRole = ConversationSearchRole.All;
                    conversationTerminal = default;
                    ResetConversationRanges();
                    conversationSearchTarget = null;
                }
                conversationSearchStatus = exception switch
                {
                    ArgumentException => "InvalidQuery, InvalidRole, InvalidTerminalFilter, InvalidRange, InvalidCorrelation or CursorMismatch. Request/task IDs require canonical lowercase nonempty D GUIDs (empty means Any). Use ordered inclusive sequence bounds 1-"
                        + ConversationText.MaximumSessionMessages.ToString(CultureInfo.InvariantCulture)
                        + " and canonical UTC yyyy-MM-ddTHH:mm:ss.fffffff+00:00 (empty means Any). Select defined roles and Any or exact typed terminal values; use 1-256 UTF-16 characters / 512 UTF-8 bytes with 1-32 distinct lexical terms of at most 64 characters; start fresh Search.",
                    ConversationSnapshotChangedException => "Snapshot changed. No search content; start a fresh Search.",
                    OperationCanceledException => "Search cancelled. No search content or successful observation inferred.",
                    _ => "Exact conversation search unavailable. No search content; refresh the selected source and start fresh Search.",
                };
            }
            else if (conversationSearchEpoch == epoch) { ClearConversationSearch(); }
            Failure(logger, exception.GetType().Name);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            conversationSearchBusy = false;
            conversationSearchCancellation = null;
            NotifyConversationSearch();
        }
    }

    private void NotifyConversationSearch()
    {
        if (conversationSearchTarget is { } target
            && (closed || !access.CanInspect || access.ControlRevision != conversationSearchControlRevision || selected != target))
        {
            InvalidateConversationSearch();
            conversationQuery = string.Empty;
            conversationRole = ConversationSearchRole.All;
            conversationTerminal = default;
            ResetConversationRanges();
            conversationSearchTarget = null;
            conversationSearchStatus = "Exact session/source or private ownership changed. Private search cleared; explicit fresh Search required.";
        }
        foreach (var name in new[] { nameof(ConversationQuery), nameof(ConversationRole), nameof(ConversationOutcome),
            nameof(ConversationRequestId), nameof(ConversationTaskId),
            nameof(ConversationMinimumSequence), nameof(ConversationMaximumSequence),
            nameof(ConversationSubmittedFrom), nameof(ConversationSubmittedThrough),
            nameof(ConversationReason), nameof(ConversationTaskState), nameof(ConversationOutcomeChoice),
            nameof(ConversationReasonChoice), nameof(ConversationTaskStateChoice), nameof(ConversationSearchStatus),
            nameof(ConversationSearchMessages), nameof(CanSearchConversation), nameof(CanNextConversationSearch),
            nameof(CanCancelConversationSearch) })
        { OnPropertyChanged(name); }
    }

    internal sealed record ConversationTerminalOption<T>(T? Value) where T : struct, Enum
    {
        public override string ToString() => Value?.ToString() ?? "Any";
    }
}
