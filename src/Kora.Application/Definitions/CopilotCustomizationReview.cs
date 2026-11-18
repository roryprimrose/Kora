using Kora.Core.Customizations;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using NativeSelection = Kora.Core.Dependencies.CopilotCustomizationSelection;

namespace Kora.Application.Definitions;

/// <summary>One volatile review. Reading, hosted-source consent and original-message retention remain distinct.</summary>
public sealed class CopilotCustomizationReview
{
    internal CopilotCustomizationReview(CustomizationSnapshot snapshot, NativeSelection selection,
        SessionWorkspaceEntry target, string body, RequestOrigin origin, DateTimeOffset expires)
    { Snapshot = snapshot; Selection = selection; Target = target; Body = body; Origin = origin; ExpiresAt = expires; }

    public Guid Id { get; } = Guid.NewGuid();
    public CustomizationSnapshot Snapshot { get; }
    public NativeSelection Selection { get; }
    public SessionWorkspaceEntry Target { get; }
    public string Body { get; }
    public RequestOrigin Origin { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool HostedSourceApproved { get; internal set; }
    public string Text => "ONE separately reviewed hosted copy; the original definition and its locality are unchanged.\n"
        + "Source: " + Snapshot.Source.Location + "\nFile: " + Snapshot.Files[0].Name
        + "\nKind: " + Snapshot.Source.Kind + "\nNative selection: " + Selection.Name
        + "\nTools, references, MCP, scripts, hooks, filesystem discovery and inferred agents: unavailable."
        + (Selection.IsSkill ? "\nActivation: one fixed tool-free native host agent preloads only this skill through the native provider." : "\nActivation: the SDK selects only this native custom agent.")
        + "\n\nCOMPLETE EXACT SOURCE\n" + Selection.SourceText
        + "\n\nEXACT ORIGINAL USER REQUEST\n" + Body
        + "\n\nACTIVE TARGET\n" + Target.Metadata!.Name.Value
        + "\nOriginal channel: " + Origin
        + "\n\nFirst authorize hosted disclosure of this exact source for this one request."
        + "\nThen separately confirm retention of the unchanged USER request and its answer or truthful HOST outcome in this exact Active conversation."
        + "\nNeither decision approves a network transmission. Each complete final transmission, including retries, needs its own review."
        + "\nNo automatic retry, permanent activation, new conversation or source modification.";
}
