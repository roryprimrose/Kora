using System.Globalization;
using System.Text;

using Kora.Core.Commands;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private string historyQuery = string.Empty;
    private SessionHistorySearchPage? historySearch;
    private CancellationTokenSource? historySearchCancellation;
    private SessionHistoryMetadataFilter historyMetadata;
    private long? historySearchRevision;

    public IReadOnlyList<HistoryMetadataOption<SessionHistoryKind>> HistoryKinds { get; } =
        [new(null), .. Enum.GetValues<SessionHistoryKind>().Select(value => new HistoryMetadataOption<SessionHistoryKind>(value))];
    public IReadOnlyList<HistoryMetadataOption<HostTaskState>> HistoryTaskStates { get; } =
        [new(null), .. Enum.GetValues<HostTaskState>().Select(value => new HistoryMetadataOption<HostTaskState>(value))];
    public IReadOnlyList<HistoryMetadataOption<HostInteractionOutcome>> HistoryDecisions { get; } =
        [new(null), .. Enum.GetValues<HostInteractionOutcome>().Select(value => new HistoryMetadataOption<HostInteractionOutcome>(value))];
    public IReadOnlyList<HistoryMetadataOption<QuestionStatus>> HistoryQuestionStatuses { get; } =
        [new(null), .. Enum.GetValues<QuestionStatus>().Select(value => new HistoryMetadataOption<QuestionStatus>(value))];
    public IReadOnlyList<HistoryMetadataOption<RequestOrigin>> HistoryAnswerChannels { get; } =
        [new(null), .. Enum.GetValues<RequestOrigin>().Select(value => new HistoryMetadataOption<RequestOrigin>(value))];

    public HistoryMetadataOption<SessionHistoryKind> HistoryKindChoice
    {
        get => new(historyMetadata.Kind);
        set { if (value is not null) { EditHistoryMetadata(historyMetadata with { Kind = value.Value }); } }
    }
    public HistoryMetadataOption<HostTaskState> HistoryTaskStateChoice
    {
        get => new(historyMetadata.TaskState);
        set { if (value is not null) { EditHistoryMetadata(historyMetadata with { TaskState = value.Value }); } }
    }
    public HistoryMetadataOption<HostInteractionOutcome> HistoryDecisionChoice
    {
        get => new(historyMetadata.Decision);
        set { if (value is not null) { EditHistoryMetadata(historyMetadata with { Decision = value.Value }); } }
    }
    public HistoryMetadataOption<QuestionStatus> HistoryQuestionStatusChoice
    {
        get => new(historyMetadata.QuestionStatus);
        set { if (value is not null) { EditHistoryMetadata(historyMetadata with { QuestionStatus = value.Value }); } }
    }
    public HistoryMetadataOption<RequestOrigin> HistoryAnswerChannelChoice
    {
        get => new(historyMetadata.AnswerChannel);
        set { if (value is not null) { EditHistoryMetadata(historyMetadata with { AnswerChannel = value.Value }); } }
    }

    private void EditHistoryMetadata(SessionHistoryMetadataFilter value)
    {
        if (closed || !access.CanInspect || historyMetadata == value) { return; }
        value.Validate();
        historyMetadata = value;
        RetireHistorySearch();
    }

    public string HistoryQuery
    {
        get => historyQuery;
        set
        {
            if (closed || !access.CanInspect) { return; }
            historyQuery = value;
            RetireHistorySearch();
        }
    }

    private void RetireHistorySearch()
    {
        historySearchCancellation?.Cancel();
        historySearch = null;
        history = null;
        selectedHistory = null;
        Detail = string.Empty;
        selectionEpoch++;
        historySearchRevision = access.ControlRevision;
        status = LocalFileRetrievalPolicy.TryQuery(historyQuery, out _)
            ? "Query or metadata changed; start a fresh exact-session lexical search. Predicates are AND-composed passive citations only."
            : "Enter 1-256 characters / 512 UTF-8 bytes containing 1-32 distinct whole words of at most 64 characters. Query/results are volatile.";
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Detail));
        NotifyHistory();
    }

    public void ClearHistorySearchInputs()
    {
        ClearHistorySearch();
        RetireHistorySearch();
    }

    public bool CanSearchHistory => CanHistory && !refreshingWork
        && LocalFileRetrievalPolicy.TryQuery(historyQuery, out _);
    public bool CanNextHistorySearch => CanSearchHistory && historySearch?.Next is not null;
    public bool CanCancelHistorySearch => !closed && historySearchCancellation is not null;

    public void CancelHistorySearch() => historySearchCancellation?.Cancel();

    public Task SearchHistoryAsync(bool next = false) => RunAsync(async () =>
    {
        var epoch = selectionEpoch;
        var subjectText = historySessionId;
        if (!Guid.TryParseExact(subjectText, "D", out var id) || id == Guid.Empty)
        {
            throw new InvalidOperationException("Enter the exact immutable session ID, not a name.");
        }
        var continuation = next ? historySearch?.Next
            ?? throw new InvalidOperationException("No next search snapshot page; start a fresh search.") : null;
        var query = historyQuery;
        var metadata = historyMetadata;
        var revision = access.ControlRevision;
        historySearchRevision = revision;
        historySearch = null;
        history = null;
        selectedHistory = null;
        Detail = string.Empty;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        historySearchCancellation = cancellation;
        NotifyHistory();
        try
        {
            var result = await service.SearchHistoryAsync(new(id), query, continuation, 25, cancellation.Token, metadata);
            cancellation.Token.ThrowIfCancellationRequested();
            if (closed || selectionEpoch != epoch || !string.Equals(historySessionId, subjectText, StringComparison.Ordinal)
                || !string.Equals(historyQuery, query, StringComparison.Ordinal) || historyMetadata != metadata
                || access.ControlRevision != revision)
            {
                throw new OperationCanceledException("The exact history subject or query changed; start a fresh search.");
            }
            historySearch = result;
            history = new(result.SessionId, result.Generation, result.Disposed, result.Snapshot, result.Records, null);
            Detail = Encoding.UTF8.GetString(SessionCommandResult.Serialize(new("observed", SessionHistorySearchPage.Scope)
                { HistorySearch = result }));
            status = "Passive lexical search: " + result.Records.Length.ToString(CultureInfo.InvariantCulture)
                + " matches, " + result.Scanned.ToString(CultureInfo.InvariantCulture) + " scanned, "
                + result.Gaps.ToString(CultureInfo.InvariantCulture) + " baseline/unavailable/redacted gaps, "
                + result.OmittedMatches.ToString(CultureInfo.InvariantCulture) + " oversized matching receipts omitted. "
                + (result.Next is null ? "End of snapshot." : "Continue the snapshot, including after an empty page.")
                + " Query is volatile; no activity, authority or question target changed.";
        }
        finally { historySearchCancellation = null; }
    }, passive: true);

    private void ClearHistorySearch()
    {
        historySearchCancellation?.Cancel();
        historySearch = null;
        historyQuery = string.Empty;
        historyMetadata = default;
        historySearchRevision = null;
        Detail = string.Empty;
    }

    private void NotifyHistorySearch()
    {
        if (historySearchRevision is { } revision
            && (closed || !access.CanInspect || access.ControlRevision != revision))
        {
            ClearHistorySearch();
            history = null;
            selectedHistory = null;
            selectionEpoch++;
            OnPropertyChanged(nameof(HistoryRecords));
            OnPropertyChanged(nameof(SelectedHistoryRecord));
            OnPropertyChanged(nameof(CanOpenHistoryDetail));
            OnPropertyChanged(nameof(Detail));
        }
        OnPropertyChanged(nameof(HistoryQuery));
        OnPropertyChanged(nameof(HistoryKindChoice));
        OnPropertyChanged(nameof(HistoryTaskStateChoice));
        OnPropertyChanged(nameof(HistoryDecisionChoice));
        OnPropertyChanged(nameof(HistoryQuestionStatusChoice));
        OnPropertyChanged(nameof(HistoryAnswerChannelChoice));
        OnPropertyChanged(nameof(CanSearchHistory));
        OnPropertyChanged(nameof(CanNextHistorySearch));
        OnPropertyChanged(nameof(CanCancelHistorySearch));
    }

    internal sealed record HistoryMetadataOption<T>(T? Value) where T : struct, Enum
    {
        public override string ToString() => Value?.ToString() ?? "Any";
    }
}
