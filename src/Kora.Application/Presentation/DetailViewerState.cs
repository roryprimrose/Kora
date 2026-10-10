using System.Text;
using Kora.Application.Infrastructure;
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
    private long generation = 1;
    private readonly Func<bool>? readAdmission;

    public DetailViewerState(AdmittedDetailContent content, Func<bool>? sourceReadAdmission = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        this.content = content;
        readAdmission = sourceReadAdmission;
        Reference = content.Reference;
    }

    public DetailContentReference Reference { get; }
    public AdmittedDetailContent? Content => CanRead() ? content : null;
    public long Generation => generation;
    public DetailRenderState RenderState => renderState;
    public string Status => status;
    public bool IsSource => isSource;
    public string ActiveText => !CanRead() ? string.Empty
        : isSource || renderState != DetailRenderState.Rendered ? content!.Source : renderedText;
    public string SearchQuery => searchQuery;
    public int MatchStart => matchStart;
    public int MatchLength => matchStart < 0 ? 0 : searchQuery.Length;

    public bool CompleteRender(long renderGeneration, string semanticText, string message, bool fallback)
    {
        ArgumentNullException.ThrowIfNull(semanticText);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!CanRead() || renderGeneration != generation)
        {
            return false;
        }
        if (new UTF8Encoding(false, true).GetByteCount(semanticText) > NativeDetailProfile.MaximumUtf8Bytes)
        {
            throw new InvalidDataException("The semantic text exceeds the native detail profile.");
        }
        if (!fallback && string.IsNullOrWhiteSpace(semanticText))
        {
            throw new InvalidDataException("An empty semantic projection is not successful rendering; retain the exact source fallback.");
        }
        renderedText = semanticText;
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
        if (!CanRead())
        {
            return false;
        }
        if (query.Length > NativeDetailProfile.MaximumSearchCharacters)
        {
            ReportStatus("Search is limited to 256 characters; nothing was truncated.");
            return false;
        }
        var sameQuery = string.Equals(query, searchQuery, StringComparison.Ordinal);
        searchQuery = query;
        var text = ActiveText;
        if (query.Length == 0)
        {
            matchStart = -1;
        }
        else if (backwards)
        {
            var start = sameQuery && matchStart > 0 ? matchStart - 1 : text.Length - 1;
            matchStart = text.LastIndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (matchStart < 0)
            {
                matchStart = text.LastIndexOf(query, StringComparison.OrdinalIgnoreCase);
            }
        }
        else
        {
            var start = sameQuery && matchStart >= 0 ? matchStart + query.Length : 0;
            matchStart = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (matchStart < 0)
            {
                matchStart = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            }
        }
        ReportStatus(matchStart < 0 ? "No search match." : "Search match in the current immutable revision.");
        OnPropertyChanged(nameof(SearchQuery));
        OnPropertyChanged(nameof(MatchStart));
        OnPropertyChanged(nameof(MatchLength));
        return matchStart >= 0;
    }

    public bool TryGetCopySource(bool canAccess, bool disclosureConfirmed, out string? source)
    {
        source = null;
        if (!CanRead() || !canAccess)
        {
            ReportStatus("Copy blocked: this exact revision is closed or no longer accessible.");
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
        renderedText = string.Empty;
        generation++;
        renderState = DetailRenderState.Closed;
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

    private void ResetSearch()
    {
        searchQuery = string.Empty;
        matchStart = -1;
        OnPropertyChanged(nameof(SearchQuery));
        OnPropertyChanged(nameof(MatchStart));
        OnPropertyChanged(nameof(MatchLength));
    }

    private void NotifyPresentation()
    {
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(Generation));
        OnPropertyChanged(nameof(RenderState));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsSource));
        OnPropertyChanged(nameof(ActiveText));
    }
}
