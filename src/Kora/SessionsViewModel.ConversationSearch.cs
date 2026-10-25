using System.Globalization;

using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private string conversationQuery = string.Empty;
    private ConversationSearchRole conversationRole;
    private string conversationSubmittedFrom = string.Empty;
    private string conversationSubmittedThrough = string.Empty;
    private HostTaskState? conversationTaskState;
    private ConversationSearchPage? conversationSearchPage;
    private CancellationTokenSource? conversationSearchCancellation;
    private long conversationSearchEpoch;
    private bool conversationSearchBusy;
    private SessionWorkspaceEntry? conversationSearchTarget;
    private SessionWorkspaceEntry? conversationSearchDetailTarget;
    private long conversationSearchControlRevision;
    private string conversationSearchStatus = "Search only this exact session's retained conversation. All searches USER/HOST only; choose ASSISTANT for retained answers. No model context or receipt-history search.";

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
    public string ConversationSubmittedFrom
    {
        get => conversationSubmittedFrom;
        set => EditConversationDate(ref conversationSubmittedFrom, value);
    }
    public string ConversationSubmittedThrough
    {
        get => conversationSubmittedThrough;
        set => EditConversationDate(ref conversationSubmittedThrough, value);
    }
    public IReadOnlyList<ConversationTaskStateOption> ConversationTaskStates { get; } =
        [new(null), .. Enum.GetValues<HostTaskState>().Select(value => new ConversationTaskStateOption(value))];
    public ConversationTaskStateOption ConversationTaskStateChoice
    {
        get => new(conversationTaskState);
        set
        {
            if (closed || !access.CanInspect || value is null || conversationTaskState == value.Value) { return; }
            conversationTaskState = value.Value;
            RetireConversationFilter();
        }
    }

    private void EditConversationDate(ref string field, string value)
    {
        if (closed || !access.CanInspect || string.Equals(field, value, StringComparison.Ordinal)) { return; }
        field = value;
        RetireConversationFilter();
    }

    private void RetireConversationFilter()
    {
        InvalidateConversationSearch();
        conversationSearchTarget = selected;
        conversationSearchControlRevision = access.ControlRevision;
        conversationSearchStatus = "Submitted date or task state edited. Explicit Search required; empty dates and Any state add no restriction.";
        NotifyConversationSearch();
    }

    private void ResetConversationFilters()
    {
        conversationSubmittedFrom = string.Empty;
        conversationSubmittedThrough = string.Empty;
        conversationTaskState = null;
    }

    private static ConversationRangeFilter ParseConversationDates(string from, string through)
    {
        try { return ConversationRangeFilter.Parse(string.Empty, string.Empty, from, through); }
        catch (InvalidDataException exception)
        { throw new ArgumentException("Submitted date bounds require ordered canonical UTC timestamps.", nameof(from), exception); }
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
    public bool CanSearchConversation => CanReadConversation && !conversationSearchBusy;
    public bool CanNextConversationSearch => CanSearchConversation && conversationSearchPage?.Next is not null;
    public bool CanCancelConversationSearch => conversationSearchBusy;

    public void ClearConversationSearch()
    {
        InvalidateConversationSearch();
        conversationQuery = string.Empty;
        conversationRole = ConversationSearchRole.All;
        ResetConversationFilters();
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
        var submittedFrom = conversationSubmittedFrom;
        var submittedThrough = conversationSubmittedThrough;
        var taskState = conversationTaskState;
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
            var range = ParseConversationDates(submittedFrom, submittedThrough);
            var result = await conversation!.SearchAsync(target.Authority.SessionId, query, cursor,
                ConversationSearchPage.DefaultRecords, cancellation.Token, role, new(TaskState: taskState), range);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Live() || result.Session != target.Authority)
            { throw new OperationCanceledException("Exact private conversation search was revoked."); }
            conversationSearchPage = result;
            conversationSearchStatus = "Observed " + result.Messages.Length.ToString(CultureInfo.InvariantCulture)
                + " exact " + result.Role + " field matches; scanned " + result.Scanned.ToString(CultureInfo.InvariantCulture)
                + ", omitted 0, unavailable 0. "
                + (result.Next is null ? "End of original sequence snapshot." : "Next continues the original snapshot.")
                + " Complete chronological retained text; ASSISTANT matches are inert saved answers, not new generation, ranking or execution authority.";
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
                    ResetConversationFilters();
                    conversationSearchTarget = null;
                }
                conversationSearchStatus = exception switch
                {
                    ArgumentException => "InvalidQuery, InvalidRole, InvalidTaskState, InvalidRange or CursorMismatch. Use ordered inclusive UTC dates in yyyy-MM-ddTHH:mm:ss.fffffff+00:00 format (empty means Any), a defined task state and 1-256 UTF-16 characters / 512 UTF-8 bytes with 1-32 distinct lexical terms of at most 64 characters; start a fresh Search.",
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
            ResetConversationFilters();
            conversationSearchTarget = null;
            conversationSearchStatus = "Exact session/source or private ownership changed. Private search cleared; explicit fresh Search required.";
        }
        foreach (var name in new[] { nameof(ConversationQuery), nameof(ConversationRole),
            nameof(ConversationSubmittedFrom), nameof(ConversationSubmittedThrough), nameof(ConversationTaskStateChoice),
            nameof(ConversationSearchStatus), nameof(ConversationSearchMessages),
            nameof(CanSearchConversation), nameof(CanNextConversationSearch), nameof(CanCancelConversationSearch) })
        { OnPropertyChanged(name); }
    }

    internal sealed record ConversationTaskStateOption(HostTaskState? Value)
    {
        public override string ToString() => Value?.ToString() ?? "Any";
    }
}
