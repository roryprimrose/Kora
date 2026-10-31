using System.Text;
using Kora.Application.Infrastructure;
using Kora.Core.Context;
using Kora.Core.Presentation;

namespace Kora.Application.Presentation;

public sealed class DetailViewerState : ObservableObject
{
    private AdmittedDetailContent? content;
    private string renderedText = string.Empty;
    private string status = "Rendering native-text-v1.";
    private DetailRenderState renderState = DetailRenderState.Rendering;
    private bool isSource;
    private string searchQuery = string.Empty;
    private int matchStart = -1;
    private SnapshotLiteralSpan? selected;
    private int? matchCount;
    private int? matchOrdinal;
    private string findSummary = "Count unavailable; choose Find first.";
    private long findRevision;
    private bool findHost;
    private long generation = 1;
    private Func<bool>? readAdmission;
    private Func<bool>? findAdmission;
    private Guid renderToken;
    private Func<CancellationToken, Task<bool>>? validateSource;

    public DetailViewerState(AdmittedDetailContent content, Func<bool>? sourceReadAdmission = null,
        Func<bool>? findAdmission = null, Func<CancellationToken, Task<bool>>? validateSource = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        this.content = content;
        readAdmission = sourceReadAdmission;
        this.findAdmission = findAdmission;
        this.validateSource = validateSource;
        Reference = content.Reference;
        status = $"Rendering {content.Profile}.";
    }

    public DetailContentReference Reference { get; }
    public AdmittedDetailContent? Content => CanRead() ? content : null;
    public long Generation => generation;
    public DetailRenderState RenderState => renderState;
    public string Status => status;
    public bool IsSource => isSource;
    public string ActiveText => !CanRead() ? string.Empty
        : isSource || renderState != DetailRenderState.Rendered ? content!.Source : renderedText;
    public string SearchQuery => CanFind() ? searchQuery : string.Empty;
    public int MatchStart => CanFind() ? matchStart : -1;
    public int MatchLength => MatchStart < 0 ? 0 : searchQuery.Length;
    public int? MatchCount => CanFind() ? matchCount : null;
    public int? MatchOrdinal => CanFind() ? matchOrdinal : null;
    public string FindSummary => CanFind() ? findSummary : "Literal Find unavailable.";
    public bool FindHost => findHost;
    public bool FindAvailable => CanFind();
    public bool CanNavigateFind => FindAvailable && selected is not null;
    public string HostText => !CanRead() || content!.ConversationMessage is not { } message ? string.Empty
        : $"Separate HOST observation: {message.Outcome?.ToString() ?? "Pending / inspect task recovery"}"
            + $" · Reason: {message.Reason?.ToString() ?? "None recorded"} · Task status: {message.ExpectedTaskState}"
            + " · Not an assistant answer or execution permission.";
    public string FindText => findHost ? HostText : ActiveText;
    public string AssistantText => !CanRead() || content!.Origin == DetailContentOrigin.RetainedAnswerContext
        ? string.Empty : content.ConversationMessage?.AssistantResponse?.Body ?? string.Empty;

    public bool CanInteractWithFinalDocument => CanRead() && readAdmission is not null
        && renderState == DetailRenderState.Rendered && content!.Kind == DetailContentKind.Markdown
        && content.Origin is DetailContentOrigin.FinalizedResponse or DetailContentOrigin.RetainedAssistant;

    public FinalDocumentSource CaptureFinalDocument()
    {
        if (!CanInteractWithFinalDocument)
        { throw new InvalidOperationException("Exact host-admitted rendered final Markdown is required for document interaction."); }
        return new(this, content!, renderToken);
    }

    internal bool IsFinalDocumentCurrent(FinalDocumentSource source) =>
        // This owner never replaces content; every render and closure rotates its token.
        CanRead() && renderToken == source.RenderToken;

    internal bool RequiresSourceValidation => validateSource is not null;

    internal async Task<bool> ValidateFinalDocumentAsync(FinalDocumentSource source, CancellationToken token)
    {
        if (token.IsCancellationRequested || !IsFinalDocumentCurrent(source)) { return false; }
        if (validateSource is { } validate && !await validate(token)) { Close(); return false; }
        return !token.IsCancellationRequested && IsFinalDocumentCurrent(source);
    }

    public bool CompleteRender(long renderGeneration, string semanticText, string message, bool fallback)
    {
        ArgumentNullException.ThrowIfNull(semanticText);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!CanRead() || renderGeneration != generation)
        {
            return false;
        }
        var limit = !fallback && content!.Kind == DetailContentKind.StaticHtml
            ? StaticHtmlProfile.MaximumSemanticUtf8Bytes : NativeDetailProfile.MaximumUtf8Bytes;
        if (new UTF8Encoding(false, true).GetByteCount(semanticText) > limit)
        {
            throw new InvalidDataException("The semantic text exceeds the native detail profile.");
        }
        if (!fallback && string.IsNullOrWhiteSpace(semanticText))
        {
            throw new InvalidDataException("An empty semantic projection is not successful rendering; retain the exact source fallback.");
        }
        renderedText = semanticText;
        renderToken = Guid.NewGuid();
        renderState = fallback ? DetailRenderState.SourceFallback : DetailRenderState.Rendered;
        status = message;
        ResetSearch();
        NotifyPresentation();
        return true;
    }

    public void SetSource(bool source)
    {
        if (!CanRead())
        {
            return;
        }
        isSource = source;
        ResetSearch();
        NotifyPresentation();
    }

    public bool Search(string query, bool backwards = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        var sameQuery = string.Equals(query, searchQuery, StringComparison.Ordinal);
        return Find(query, !sameQuery || selected is null ? SnapshotFindDirection.First
            : backwards ? SnapshotFindDirection.Previous : SnapshotFindDirection.Next);
    }

    public void SetFindHost(bool host)
    {
        if (!CanFind()) { return; }
        if (host && content!.ConversationMessage is null)
        {
            CancelFind();
            ReportStatus("Separate typed HOST Details are unavailable for this admitted snapshot.");
            return;
        }
        findHost = host;
        ResetSearch();
        NotifyPresentation();
    }

    public void CancelFind(bool clearQuery = false)
    {
        ResetFindResult();
        if (clearQuery) { searchQuery = string.Empty; }
        NotifyFind();
        if (CanFind()) { ReportStatus(clearQuery ? "Literal Find cleared." : "Literal Find cancelled; no match or focus claimed."); }
    }

    public void EditFindQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!CanFind()) { return; }
        searchQuery = query;
        CancelFind();
    }

    public bool Find(string query, SnapshotFindDirection direction)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!CanFind()) { return false; }
        if (!string.Equals(query, searchQuery, StringComparison.Ordinal)) { EditFindQuery(query); }
        if (!CanFind() || !string.Equals(query, searchQuery, StringComparison.Ordinal)) { return false; }
        var previous = selected;
        if (!SnapshotLiteralFind.IsValidQuery(query))
        {
            CancelFind();
            ReportStatus("Literal Find rejected: enter nonempty, bounded, well-formed one-line text without unsafe controls; nothing was truncated.");
            return false;
        }
        if (direction != SnapshotFindDirection.First && previous is null)
        {
            CancelFind();
            ReportStatus("Literal Find unavailable: choose Find first before navigating.");
            return false;
        }
        var revision = ++findRevision;
        var text = FindText;
        var (match, count, ordinal) = SnapshotLiteralFind.FindCounted(text, query, direction, previous);
        if (!CanFind() || revision != findRevision) { return false; }
        selected = match ?? (direction == SnapshotFindDirection.First ? null : previous);
        matchStart = match?.Start ?? -1;
        matchCount = count;
        matchOrdinal = selected is null ? null : ordinal;
        findSummary = count == 0 ? "0 matches in this exact admitted text."
            : $"Match {ordinal} of {count} in this exact admitted text.";
        ReportStatus(match is not null
            ? $"Exact literal match: UTF-16 [{match.Start}, {match.Start + match.Length}); end exclusive."
            : direction == SnapshotFindDirection.First ? "No literal match in this exact admitted text."
                : direction == SnapshotFindDirection.Next ? "End of forward direction; no wrap. Earlier matches may remain."
                    : "End of backward direction; no wrap. Later matches may remain.");
        NotifyFind();
        return CanFind() && revision == findRevision && matchStart >= 0;
    }

    public bool TryGetCopySource(bool canAccess, bool disclosureConfirmed, out string? source)
    {
        source = null;
        if (!CanRead() || !canAccess)
        {
            ReportStatus("Copy blocked: this exact revision is closed or no longer accessible.");
            return false;
        }
        if (content!.Origin is DetailContentOrigin.RetainedConversation or DetailContentOrigin.RetainedAssistant
            or DetailContentOrigin.RetainedLocalObservation or DetailContentOrigin.RetainedAnswerContext)
        {
            ReportStatus("Copy blocked: retained conversation Details do not grant clipboard disclosure authority.");
            return false;
        }
        if (content!.Sensitivity == DetailSensitivity.DisclosureConfirmationRequired && !disclosureConfirmed)
        {
            ReportStatus("Confirm disclosure in the native control before copying private content.");
            return false;
        }
        source = content.Source;
        return true;
    }

    public bool TryGetCopySelection(bool canAccess, bool disclosureConfirmed, int start, int length, out string? selection)
    {
        selection = null;
        if (!TryGetCopySource(canAccess, disclosureConfirmed, out _))
        {
            return false;
        }
        var text = ActiveText;
        if (start < 0 || length <= 0 || start > text.Length || length > text.Length - start)
        {
            ReportStatus("Copy blocked: select a valid nonempty range in this immutable revision.");
            return false;
        }
        selection = text.Substring(start, length);
        return true;
    }

    public void ReportStatus(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        status = message;
        OnPropertyChanged(nameof(Status));
    }

    public void Close()
    {
        content = null;
        readAdmission = null;
        findAdmission = null;
        validateSource = null;
        renderedText = string.Empty;
        renderToken = Guid.Empty;
        generation++;
        renderState = DetailRenderState.Closed;
        findHost = false;
        status = "Viewer closed; retained source and work were not changed.";
        ResetSearch();
        NotifyPresentation();
    }

    private bool CanRead()
    {
        if (content is null) { return false; }
        if (readAdmission is null || readAdmission()) { return true; }
        Close();
        return false;
    }

    private bool CanFind()
    {
        if (!CanRead()) { return false; }
        if (findAdmission is null || findAdmission()) { return true; }
        Close();
        return false;
    }

    private void ResetSearch()
    {
        searchQuery = string.Empty;
        ResetFindResult();
        NotifyFind();
    }

    private void ResetFindResult()
    {
        findRevision++;
        matchStart = -1;
        selected = null;
        matchCount = null;
        matchOrdinal = null;
        findSummary = "Count unavailable; choose Find first.";
    }

    private void NotifyFind()
    {
        OnPropertyChanged(nameof(SearchQuery));
        OnPropertyChanged(nameof(MatchStart));
        OnPropertyChanged(nameof(MatchLength));
        OnPropertyChanged(nameof(MatchCount));
        OnPropertyChanged(nameof(MatchOrdinal));
        OnPropertyChanged(nameof(FindSummary));
        OnPropertyChanged(nameof(CanNavigateFind));
        OnPropertyChanged(nameof(FindAvailable));
    }

    private void NotifyPresentation()
    {
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Generation));
        OnPropertyChanged(nameof(RenderState));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsSource));
        OnPropertyChanged(nameof(ActiveText));
        OnPropertyChanged(nameof(HostText));
        OnPropertyChanged(nameof(AssistantText));
        OnPropertyChanged(nameof(FindHost));
    }
}
