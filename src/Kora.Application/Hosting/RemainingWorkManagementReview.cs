using Kora.Core.Dependencies;
using Kora.Core.Storage;

using System.Diagnostics;

namespace Kora.Application.Hosting;

/// <summary>One exact native ledger-disclosure decision, not authority to change the observed work.</summary>
public sealed class RemainingWorkManagementReview : IDisposable
{
    private readonly CancellationTokenSource revocation = new();
    private readonly Lock gate = new();
    private Task revocationCompletion = Task.CompletedTask;
    private bool retired;
    private int consumed;

    internal RemainingWorkManagementReview(RemainingWorkManagement owner, SessionRemainingWorkAnswer source,
        SessionWorkspaceEntry destination, string question, string evidence, Func<bool> current, ActivityContext cause)
    {
        Owner = owner;
        Source = source;
        Destination = destination;
        Question = question;
        Evidence = evidence;
        Current = current;
        Revocation = revocation.Token;
        Cause = cause;
    }

    internal RemainingWorkManagement Owner { get; }
    internal SessionRemainingWorkAnswer Source { get; }
    public string Evidence { get; }
    public string ModelPolicy => RemainingWorkManagement.Policy;
    internal Func<bool> Current { get; }
    internal CancellationToken Revocation { get; }
    internal ActivityContext Cause { get; }
    internal bool TryConsume() => Interlocked.Exchange(ref consumed, 1) == 0;
    public SessionWorkspaceEntry Destination { get; }
    public string Question { get; }
    public ModelProviderSelection Provider => ModelProviderSelection.OllamaCandidate;
    public string Disclosure => RemainingWorkManagement.Disclosure
        + "\nAnswer destination: " + (Destination.Metadata is { } named ? named.Name.Value : "Unnamed conversation")
        + " | " + Destination.Authority.SessionId.Value.ToString("D")
        + "; generation " + Destination.Authority.Generation.Value
        + "; metadata revision " + (Destination.Metadata is { } versioned ? versioned.Revision.Value : 0)
        + "\nLocal catalogue selection: " + Provider.ToString()
        + "\nUnchanged question:\n" + Question
        + "\nComplete model policy:\n" + RemainingWorkManagement.Policy
        + "\nComplete body-free ledger evidence:\n" + Evidence;

    public void Dispose()
    {
        lock (gate)
        {
            if (retired) { return; }
            retired = true;
            revocationCompletion = RevokeAsync();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "Joins only this review's owned asynchronous cancellation callbacks, without blocking the native revocation or host deadline.")]
    internal Task JoinRevocationAsync()
    {
        Dispose();
        lock (gate) { return revocationCompletion; }
    }

    private async Task RevokeAsync()
    {
        try { await revocation.CancelAsync().ConfigureAwait(false); }
        finally { revocation.Dispose(); }
    }
}