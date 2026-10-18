using System.Globalization;

using Kora.Core.Authorization;
using Kora.Core.Conversation;

namespace Kora.Application.Presentation;

// A revocable historical presentation, not a saved-draft write or authority token.
public sealed class ConversationDraftComparison : IDisposable
{
    private Func<bool>? admission;
    private string? savedBody;
    private string buffer;
    private string baselineLabel;
    private string summary;

    public ConversationDraftComparison(WorkSessionAuthorization selected, ConversationDraftSnapshot observed,
        string buffer, Func<bool> current)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(current);
        observed.Draft.Validate();
        ConversationText.Validate(buffer);
        if (observed.Session != selected || observed.Draft.SessionId != selected.SessionId || selected.Generation.Value <= 0)
        { throw new InvalidDataException("Compare requires the exact observed saved draft and selected session generation."); }
        if (!current()) { throw new OperationCanceledException("Private composer comparison admission changed."); }
        admission = current;
        savedBody = observed.Draft.Body;
        this.buffer = buffer;
        baselineLabel = "Historical observed saved draft | exact session " + selected.SessionId.Value.ToString("D")
            + " | generation " + selected.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + " | observed saved revision " + observed.Draft.Revision.ToString(CultureInfo.InvariantCulture)
            + " | not current disk";
        summary = savedBody is null ? "Absent observed saved body; no equality or change conclusion."
            : string.Equals(savedBody, buffer, StringComparison.Ordinal)
                ? "Exact frozen bodies are ordinally equal."
                : "Exact frozen bodies differ.";
        summary += " Historical snapshots only; no current disk freshness, semantic safety, Save, Submit or approval claim.";
    }

    public bool Available => EnsureCurrent();
    public string? SavedBody => EnsureCurrent() ? savedBody : null;
    public string Buffer => EnsureCurrent() ? buffer : string.Empty;
    public string BaselineLabel => EnsureCurrent() ? baselineLabel : string.Empty;
    public string Summary => EnsureCurrent() ? summary : string.Empty;

    private bool EnsureCurrent()
    {
        if (admission?.Invoke() == true) { return true; }
        Dispose();
        return false;
    }

    public void Dispose()
    {
        admission = null;
        savedBody = null;
        buffer = baselineLabel = summary = string.Empty;
    }
}
