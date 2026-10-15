using System.Globalization;
using System.Text;

using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class AllWorkViewModel(
    SessionWorkspaceService service, ISessionWorkspaceAccess access, ILogger<AllWorkViewModel> logger,
    Func<HostId<SessionIdentity>, Func<bool>, CancellationToken, Task> navigate) : ObservableObject
{
    private readonly HostRequest viewer = HostRequest.Create(RequestOrigin.LocalUi);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private SessionWorkOverviewPage? page;
    private SessionWorkOverviewRow? selected;
    private SessionListFilter filter;
    private SessionWorkOverviewAvailability? availability;
    private string queryText = string.Empty;
    private SessionListSearchKind queryKind;
    private bool needsAnswer;
    private bool busy;
    private bool hadNext;
    private bool closed;
    private bool closing;
    private long epoch;
    private string status = "Refresh manually to inspect a bounded page. Opening grants nothing. " + SessionWorkOverviewPage.Scope;

    public IReadOnlyList<SessionWorkOverviewRow> Records => page?.Records ?? [];
    public SessionWorkOverviewRow? SelectedRecord => selected;
    public IReadOnlyList<SessionListFilter> Filters { get; } = Enum.GetValues<SessionListFilter>();
    public IReadOnlyList<SessionListSearchKind> QueryKinds { get; } = Enum.GetValues<SessionListSearchKind>();
    public sealed record AvailabilityChoice(SessionWorkOverviewAvailability? Value, string Label)
    {
        public override string ToString() => Label;
    }
    public IReadOnlyList<AvailabilityChoice> AvailabilityChoices { get; } =
    [
        new(null, "Any"),
        new(SessionWorkOverviewAvailability.Observed, "Observed"),
        new(SessionWorkOverviewAvailability.SourceChanged, "Source changed"),
        new(SessionWorkOverviewAvailability.ReadUnavailable, "Read unavailable"),
        new(SessionWorkOverviewAvailability.OutputBound, "Output bound")
    ];
    public string Status => status;
    public bool CanRead => !closed && !busy && access.CanInspect;
    public bool CanEdit => !closed && access.CanInspect;
    public bool CanSearch => CanEdit && (queryText.Length == 0 || SessionListSearch.IsValid(queryKind, filter, queryText));
    public bool CanNext => CanSearch && (busy ? hadNext : page?.Next is not null);
    public bool CanCancel => !closed && operation is not null;
    public bool CanNavigate => CanEdit && selected is { Availability: SessionWorkOverviewAvailability.Observed };
    public string SelectedDetails => selected is null ? string.Empty : Describe(selected);
    public string QueryHint => queryText.Length == 0
        ? "No query: bounded overview scan. Typing never reads; Search/Refresh starts a fresh scan."
        : SessionListSearch.IsValid(queryKind, filter, queryText)
            ? "Valid private metadata query. Literal case-sensitive saved name substring or exact canonical session ID; no content search."
            : "Invalid metadata query. NameSubstring requires nonblank single-line NFC text, no controls/formats or surrounding whitespace, "
                + "at most 120 scalars / 480 UTF-8 bytes. ExactId requires a nonempty canonical lowercase D GUID. No normalization or fallback.";
    public string QueryText
    {
        get => queryText;
        set
        {
            if (!CanEdit || string.Equals(queryText, value, StringComparison.Ordinal)) { return; }
            Invalidate();
            queryText = value;
            status = QueryHint;
            Notify();
        }
    }
    public SessionListSearchKind QueryKind
    {
        get => queryKind;
        set
        {
            if (!CanEdit || !Enum.IsDefined(value) || queryKind == value) { return; }
            Invalidate();
            queryKind = value;
            status = QueryHint;
            Notify();
        }
    }
    public void ClearQuery()
    {
        if (!CanEdit) { return; }
        Invalidate();
        queryText = string.Empty;
        status = "Private metadata query cleared. Refresh starts a fresh bounded scan; no read was performed.";
        Notify();
    }
    public SessionListFilter Filter
    {
        get => filter;
        set
        {
            if (!CanEdit || !Enum.IsDefined(value) || filter == value) { return; }
            Invalidate();
            filter = value;
            status = "Filter changed. Refresh starts a fresh bounded metadata scan.";
            Notify();
        }
    }
    public bool NeedsAnswer
    {
        get => needsAnswer;
        set
        {
            if (!CanEdit || needsAnswer == value) { return; }
            Invalidate();
            needsAnswer = value;
            status = "Needs-answer filter changed. Observed pending metadata only; unavailable rows remain explicit. Refresh.";
            Notify();
        }
    }
    public SessionWorkOverviewAvailability? Availability
    {
        get => availability;
        set
        {
            SessionWorkOverviewFilter.Validate(value);
            if (!CanEdit || availability == value) { return; }
            Invalidate();
            availability = value;
            status = "Observation availability filter changed. No read performed; Refresh. Unavailable is not empty work.";
            Notify();
        }
    }
    public AvailabilityChoice SelectedAvailabilityChoice
    {
        get => AvailabilityChoices.Single(choice => choice.Value == availability);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Availability = value.Value;
        }
    }

    public void Select(SessionWorkOverviewRow? row)
    {
        if (closed || busy || row is not null && !Records.Contains(row)) { return; }
        selected = row;
        Notify();
    }
    public void Cancel() => operation?.Cancel();

    public Task RefreshAsync(bool next = false) => RunAsync(async token =>
    {
        var cursor = next ? page?.Next ?? throw new InvalidOperationException("Refresh before Next.") : null;
        var query = queryText.Length == 0 ? null : new SessionListSearch(queryKind, filter, queryText);
        ClearPage();
        var result = await service.ReadWorkOverviewAsync(filter, needsAnswer, cursor, token, query, availability);
        token.ThrowIfCancellationRequested();
        page = result;
        status = "This page: " + result.Records.Length.ToString(CultureInfo.InvariantCulture) + " groups; "
            + result.Scanned.ToString(CultureInfo.InvariantCulture) + " consumed/scanned; "
            + result.Nonmatches.ToString(CultureInfo.InvariantCulture) + " filter nonmatches; "
            + result.Records.Count(row => row.Availability != SessionWorkOverviewAvailability.Observed)
                .ToString(CultureInfo.InvariantCulture) + " explicitly unavailable. "
            + (result.OutputLimited ? "Complete byte bound reached; Next retries the unconsumed session. "
                : result.Next is not null ? "Session scan bound reached; Next continues even with zero matches. "
                    : "End of current metadata observation; not a global all-clear. ")
            + SessionWorkOverviewPage.Scope;
    });

    public Task NavigateAsync() => RunAsync(async token =>
    {
        var target = selected ?? throw new InvalidOperationException("Select an observed exact session.");
        var admittedEpoch = epoch;
        var revision = access.ControlRevision;
        bool Admitted() => !closed && access.CanInspect && revision == access.ControlRevision
            && epoch == admittedEpoch && selected == target;
        await navigate(target.Session.Authority.SessionId, Admitted, token);
        token.ThrowIfCancellationRequested();
        if (!Admitted()) { throw new OperationCanceledException("The exact navigation source changed."); }
        status = "Fresh passive exact-session navigation. No resume, queue action, cancellation target, voice/question retarget or activity renewal.";
    });

    private async Task RunAsync(Func<CancellationToken, Task> read)
    {
        if (closed || busy) { return; }
        if (!access.CanInspect) { Close(); return; }
        var revision = access.ControlRevision;
        var admittedEpoch = epoch;
        hadNext = page?.Next is not null;
        busy = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = cancellation;
        using var activity = HostActivity.BeginRoot(viewer, HostActivityLayer.Desktop, HostOperation.Request);
        Notify();
        try
        {
            await read(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (closed || !access.CanInspect || revision != access.ControlRevision || admittedEpoch != epoch)
            {
                throw new OperationCanceledException("Private overview admission/source changed.");
            }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            ClearPage();
            if (!closed) { status = "Overview cancelled/source changed. No late or partial page is current; Refresh."; }
            activity.Complete(HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ClearPage();
            if (!closed) { status = "Overview unavailable: " + exception.GetType().Name + ". No empty success or all-clear; Refresh with private source admission."; }
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
        }
        finally
        {
            operation = null;
            busy = false;
            Notify();
            if (closed && !closing) { lifetime.Dispose(); }
        }
    }

    internal void Revoke()
    {
        if (closed) { return; }
        Invalidate();
        status = "Session lifecycle, disposition or retention source changed. Page and continuation revoked; Refresh.";
        Notify();
    }
    private void ClearPage() { page = null; selected = null; }
    private void Invalidate() { epoch++; hadNext = false; operation?.Cancel(); ClearPage(); }
    public void Close()
    {
        if (closed) { return; }
        closed = true;
        closing = true;
        epoch++;
        ClearPage();
        filter = SessionListFilter.All;
        availability = null;
        queryText = string.Empty;
        queryKind = SessionListSearchKind.NameSubstring;
        needsAnswer = false;
        status = string.Empty;
        lifetime.Cancel();
        closing = false;
        if (!busy) { lifetime.Dispose(); }
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(Records));
        OnPropertyChanged(nameof(SelectedRecord));
        OnPropertyChanged(nameof(SelectedDetails));
        OnPropertyChanged(nameof(Filter));
        OnPropertyChanged(nameof(Availability));
        OnPropertyChanged(nameof(SelectedAvailabilityChoice));
        OnPropertyChanged(nameof(QueryText));
        OnPropertyChanged(nameof(QueryKind));
        OnPropertyChanged(nameof(QueryHint));
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(NeedsAnswer));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CanRead));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanNavigate));
    }

    private static string Describe(SessionWorkOverviewRow row)
    {
        var text = new StringBuilder().Append("Exact session ").Append(row.Session.Authority.SessionId.Value.ToString("D"))
            .Append(" | generation ").Append(row.Session.Authority.Generation.Value)
            .Append(" | stored ").Append(row.Session.Authority.IsActive ? "Active" : "Done")
            .Append(" | ").AppendLine(row.Availability.ToString());
        if (row.Work is not { } work)
        {
            return text.Append("Current work unavailable; not an empty queue. OutputBound means the complete session projection was omitted, never truncated.").ToString();
        }
        text.Append("Observed ").Append(work.ObservedAt.ToString("O", CultureInfo.InvariantCulture))
            .Append(" | authority revision ").Append(work.AuthorityRevision).Append(" | queue revision ").AppendLine(work.QueueRevision.ToString(CultureInfo.InvariantCulture))
            .Append("Fixed-profile effective slots ").Append(work.ExecutionSlots).Append(" | observed current fixed reads in this session ")
            .Append(work.ObservedCurrentFixedReads).Append(" | pending ").Append(work.PendingCount).Append('/').AppendLine(work.PendingCapacity.ToString(CultureInfo.InvariantCulture))
            .Append("Omitted: queue receipts ").Append(work.OmittedQueueRecords).Append(", tasks ").Append(work.OmittedTasks)
            .Append(", pending questions ").AppendLine(work.OmittedQuestions.ToString(CultureInfo.InvariantCulture));
        if (work.DispatchControl is { } control)
        {
            text.Append("Dispatch run ").Append(control.RunId.ToString("D")).Append(" | pause revision ").Append(control.Revision)
                .Append(" | paused ").Append(control.Paused).Append(" | held ").Append(control.Held)
                .Append(" | ").AppendLine(control.Reason);
        }
        foreach (var observation in work.QueueRecords)
        {
            var entry = observation.Entry;
            text.Append("Queue task ").Append(entry.Request.TaskId.Value.ToString("D")).Append(" | entry revision ").Append(entry.Revision.Value)
                .Append(" | ").Append(entry.State).Append(" | reason ").Append(observation.Eligibility)
                .Append(" | original arrival ").Append(entry.Position).Append(" | pending dispatch order ").Append(observation.PendingOrder?.ToString(CultureInfo.InvariantCulture) ?? "not pending")
                .Append(" | dependency ").Append(entry.Dependency?.Value.ToString("D") ?? "none")
                .Append(" | pending expiry ").Append(entry.ExpiresAt.ToString("O", CultureInfo.InvariantCulture))
                .Append(" | active deadline ").AppendLine(observation.ActiveDeadline?.ToString("O", CultureInfo.InvariantCulture) ?? "not admitted");
        }
        foreach (var task in work.Tasks)
        {
            text.Append("Durable task ").Append(task.Request.TaskId.Value.ToString("D")).Append(" | revision ").Append(task.Revision.Value)
                .Append(" | generation ").Append(task.Generation.Value).Append(" | ").Append(task.State)
                .Append(" | current source ").AppendLine(task.CurrentSource.ToString());
        }
        foreach (var question in work.PendingQuestions)
        {
            text.Append("Observed question ").Append(question.Key.QuestionId.Value.ToString("D")).Append(" | task ")
                .Append(question.Key.Request.TaskId.Value.ToString("D")).Append(" | revision ").Append(question.Key.Revision.Value)
                .Append(" | generation ").Append(question.Generation.Value).Append(" | ").Append(question.Status).Append(" | ").Append(question.Kind)
                .Append(" | source ").Append(question.Key.Request.Origin).Append(" | answer channel ").Append(question.AnswerChannel)
                .Append(" | expiry ").AppendLine(question.ExpiresAt.ToString("O", CultureInfo.InvariantCulture));
        }
        return text.Append("Question IDs are observed metadata only. Reply in the separate freshly admitted native question window.").ToString();
    }
}
