using System.Globalization;

namespace Kora.Application.Hosting;

/// <summary>Body-free current-run ownership observations, never cancellation authority or durable completion proof.</summary>
public sealed record SubmittedWorkActivity(
    DateTimeOffset ObservedAt, bool Available, bool CompletionUnconfirmed,
    int Busy, int Waiting, int Cancelling, int Retiring, int Pending, int Accepting, int HeldConversations,
    bool ApplicationHeld)
{
    public string Format()
    {
        if (!Available)
        {
            return "Submitted activity unavailable. No idle, completion or resource release is inferred. "
                + "Restore private host access and refresh; inspect exact retained work without replay.";
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"Submitted activity observed {ObservedAt:O}: busy {Busy}; waiting for resources {Waiting}; "
            + $"cancelling {Cancelling}; retiring {Retiring}; queued {Pending}; accepting {Accepting}; "
            + $"held conversations {HeldConversations}; application hold {(ApplicationHeld ? "held" : "not observed")}. ")
            + (CompletionUnconfirmed
                ? "Resource release or terminal retention unavailable/unconfirmed. Owned work remains held; no retry or all-clear is inferred. "
                : string.Empty)
            + "Cancelling is a request, not a joined stop. Retiring is still owned. "
            + "Zero current owners does not resolve retained Unknown or Interrupted work. "
            + "Show sessions: exact cancellation, Resume or manual-hold release. Stop All needs separate confirmation. "
            + "Submitted conversations only, not setup, volatile previews or listening.";
    }
}
