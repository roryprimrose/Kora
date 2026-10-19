using System.Globalization;

using Kora.Core.Conversation;
using Kora.Core.Storage;

namespace Kora.Application.Presentation;

/// <summary>Revocable inert review data, never a draft write or reusable confirmation authority.</summary>
public sealed class ConversationDraftRecoveryReview : IDisposable
{
    private Func<bool>? current;
    private string? oldBody;
    private string? latestBody;
    private string buffer;
    private string summary;

    internal ConversationDraftRecoveryReview(SessionWorkspaceEntry target, ConversationDraftSnapshot original,
        ConversationDraftSnapshot latest, string raw, Func<bool> admitted)
    {
        RequireObservation(target, original);
        RequireObservation(target, latest);
        ConversationText.Validate(raw);
        if (latest.Draft.Revision < original.Draft.Revision
            || latest.Draft.Revision == original.Draft.Revision && latest.Draft != original.Draft)
        { throw new InvalidDataException("The saved draft source regressed or changed without an exact revision. Reconcile the source explicitly."); }
        if (!admitted()) { throw new OperationCanceledException("The private draft recovery observation expired."); }
        current = admitted;
        oldBody = original.Draft.Body;
        latestBody = latest.Draft.Body;
        buffer = raw;
        summary = "Exact existing Active session " + target.Authority.SessionId.Value.ToString("D")
            + " | generation " + target.Authority.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + " | old observed saved revision " + original.Draft.Revision.ToString(CultureInfo.InvariantCulture)
            + " | latest observed saved revision " + latest.Draft.Revision.ToString(CultureInfo.InvariantCulture)
            + ". Complete inert old/latest/raw snapshots; absent is not empty. "
            + "Adopt rereads the exact latest source and changes only the memory baseline/expected revision. "
            + "Raw buffer retained exactly; no merge, Save, Submit, retention renewal, approval or review-use authority. "
            + "Later Save performs its own fresh exact optimistic check.";
    }

    internal static void RequireObservation(SessionWorkspaceEntry target, ConversationDraftSnapshot observation)
    {
        observation.Draft.Validate();
        if (!target.Authority.IsActive || target.Authority.Generation.Value <= 0
            || observation.Session != target.Authority || observation.Draft.SessionId != target.Authority.SessionId
            || observation.ExpectedTarget is { } expected && expected != target)
        { throw new InvalidDataException("Recovery requires an admitted exact Active session, generation and saved draft source."); }
    }

    public bool Available => EnsureCurrent();
    public string? OldBody => EnsureCurrent() ? oldBody : null;
    public string? LatestBody => EnsureCurrent() ? latestBody : null;
    public string Buffer => EnsureCurrent() ? buffer : string.Empty;
    public string Summary => EnsureCurrent() ? summary : string.Empty;

    private bool EnsureCurrent()
    {
        if (current?.Invoke() == true) { return true; }
        Dispose();
        return false;
    }

    public void Dispose()
    {
        current = null;
        oldBody = latestBody = null;
        buffer = summary = string.Empty;
    }
}
