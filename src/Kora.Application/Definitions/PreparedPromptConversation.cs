using System.Diagnostics;

using Kora.Core.Storage;

namespace Kora.Application.Definitions;

/// <summary>One in-memory review; source provenance is not written alongside the approved user body.</summary>
public sealed class PreparedPromptConversation
{
    private int consumed;
    internal PreparedPromptConversation(PromptParameterDraft draft, ExpandedPromptRequest expanded, SessionWorkspaceEntry target,
        ActivityLink cause)
    { Draft = draft; Expanded = expanded; Target = target; Cause = cause; }
    internal PromptParameterDraft Draft { get; }
    public ExpandedPromptRequest Expanded { get; }
    public SessionWorkspaceEntry Target { get; }
    internal ActivityLink Cause { get; }
    internal bool TryConsume() => Interlocked.Exchange(ref consumed, 1) == 0 && Draft.ClaimConversationUse(Expanded);
}
