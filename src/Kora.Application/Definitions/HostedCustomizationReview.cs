using Kora.Core.Customizations;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Dependencies;
using Kora.Core.Providers;

using NativeSelection = Kora.Core.Dependencies.CopilotCustomizationSelection;

namespace Kora.Application.Definitions;

/// <summary>One volatile review. Reading, hosted-source consent and original-message retention remain distinct.</summary>
public sealed class HostedCustomizationReview
{
    internal HostedCustomizationReview(CustomizationSnapshot snapshot, NativeSelection selection,
        SessionWorkspaceEntry target, string body, RequestOrigin origin, DateTimeOffset expires, ProviderService provider)
    {
        Snapshot = snapshot; NativeSelection = selection; Target = target; Body = body; Origin = origin; ExpiresAt = expires;
        Provider = provider;
        SelectedCopy = provider == ProviderService.Claude
            ? new(snapshot.Source.Kind, selection.SourceId, selection.Revision, selection.Name, selection.Description, selection.SourceText) : null;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public CustomizationSnapshot Snapshot { get; }
    internal NativeSelection NativeSelection { get; }
    public SelectedHostedDefinition? SelectedCopy { get; }
    public ProviderService Provider { get; }
    public SessionWorkspaceEntry Target { get; }
    public string Body { get; }
    public RequestOrigin Origin { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool HostedSourceApproved { get; internal set; }
    public string Text => "ONE separately reviewed hosted copy; the original definition and its locality are unchanged.\n"
        + "Source: " + Snapshot.Source.Location + "\nFile: " + Snapshot.Files[0].Name
        + "\nKind: " + Snapshot.Source.Kind + "\n" + (Provider == ProviderService.Claude ? "Claude USER copy: " : "Native selection: ") + NativeSelection.Name
        + "\nTools, references, MCP, scripts, hooks, filesystem discovery and inferred agents: unavailable."
        + (Provider == ProviderService.Claude
            ? "\nNo native Claude agent or skill activation. The complete source is lower-trust USER data, never SystemPolicy. The unchanged original USER request is the final task message."
            : NativeSelection.IsSkill ? "\nActivation: one fixed tool-free native host agent preloads only this skill through the native provider." : "\nActivation: the SDK selects only this native custom agent.")
        + "\n\nCOMPLETE EXACT SOURCE\n" + NativeSelection.SourceText
        + "\n\nEXACT ORIGINAL USER REQUEST\n" + Body
        + "\n\nACTIVE TARGET\n" + Target.Metadata!.Name.Value
        + "\nOriginal channel: " + Origin
        + "\n\nFirst authorize hosted disclosure of this exact source for this one request."
        + "\nThen separately confirm retention of the unchanged USER request and its answer or truthful HOST outcome in this exact Active conversation."
        + "\nNeither decision approves a network transmission. Each complete final transmission, including retries, needs its own review."
        + "\nNo automatic retry, permanent activation, new conversation or source modification.";
}
