using Kora.Core.Conversation;
using Kora.Core.Storage;

using System.Diagnostics;

namespace Kora.Application.Presentation;

/// <summary>Issued only by the original reviewed request owner, never reconstructed from retained content.</summary>
public sealed class DetailsOfferRequest
{
    private volatile bool retired;
    private volatile bool revealConfirmed;
    private int attempted;
    private ActivityLink[] causes;
    internal DetailsOfferRequest(ConversationSubmissionReceipt receipt, SessionWorkspaceEntry target,
        Guid optIn, Func<bool> admitted, IEnumerable<ActivityLink>? causes = null)
    { Receipt = receipt; Target = target; OptIn = optIn; IsCurrent = () => !retired && admitted(); this.causes = causes?.ToArray() ?? []; }

    public ConversationSubmissionReceipt Receipt { get; }
    public SessionWorkspaceEntry Target { get; }
    internal Guid OptIn { get; }
    internal Func<bool> IsCurrent { get; }
    internal IReadOnlyList<ActivityLink> Causes => Volatile.Read(ref causes);
    internal void LinkAnswer(ActivityContext cause) => Volatile.Write(ref causes, [.. causes, new(cause)]);
    internal bool TryConsume() => Interlocked.Exchange(ref attempted, 1) == 0;
    public bool IsRevealConfirmed => revealConfirmed;
    public Kora.Core.Hosting.HostRequest? PresentationRequest { get; private set; }
    internal void BindPresentation(Kora.Core.Hosting.HostRequest request) => PresentationRequest = request;
    public event Action? Withdrawn;
    internal void ConfirmReveal() => revealConfirmed = true;
    internal void Finish()
    {
        if (!revealConfirmed) { retired = true; Withdrawn?.Invoke(); }
        Withdrawn = null;
    }
}