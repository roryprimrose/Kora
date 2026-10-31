using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Voice;

namespace Kora.Application.Hosting;

/// <summary>Host-captured one-shot presentation, bound only to the new request that consumes this review.</summary>
public sealed class TaskResponseOutputReview
{
    private readonly Func<bool> admitted;
    private readonly Func<ConversationSubmissionReceipt, TaskResponseOutputReview, Func<bool>, CancellationToken, Task> present;
    private HostRequest? request;
    private int consumed;

    internal TaskResponseOutputReview(TaskResponseMode declared, ResponseOutputMode inherited, Func<bool> admitted,
        Func<ConversationSubmissionReceipt, TaskResponseOutputReview, Func<bool>, CancellationToken, Task> present)
    {
        if (!Enum.IsDefined(declared)) { throw new ArgumentOutOfRangeException(nameof(declared)); }
        if (!Enum.IsDefined(inherited)) { throw new ArgumentOutOfRangeException(nameof(inherited)); }
        Declared = declared;
        Configured = declared switch
        {
            TaskResponseMode.VisualOnly => ResponseOutputMode.VisualOnly,
            TaskResponseMode.Hybrid => ResponseOutputMode.Hybrid,
            TaskResponseMode.VoiceOnly => ResponseOutputMode.VoiceOnly,
            _ => inherited,
        };
        this.admitted = admitted;
        this.present = present;
    }

    public TaskResponseMode Declared { get; }
    public ResponseOutputMode Configured { get; }
    public string Status { get; internal set; } = "Task output pending; no presentation or speech is claimed.";
    internal bool IsCurrent => admitted() && Volatile.Read(ref consumed) == 0;

    internal void Bind(HostRequest exact)
    {
        if (!IsCurrent || Interlocked.CompareExchange(ref request, exact, null) is not null)
        { throw new InvalidOperationException("The exact one-task output review was retired or already bound."); }
    }

    internal async Task PresentAsync(ConversationSubmissionReceipt receipt, SessionWorkspaceEntry target,
        Func<bool> sourceCurrent, CancellationToken token)
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0 || request != receipt.Message.Request
            || request != receipt.Task.Request || target.Authority.SessionId != request.SessionId
            || target.Authority.Generation != receipt.Message.Generation || receipt.Message.Outcome is null)
        { throw new InvalidOperationException("Only the exact confirmed terminal task may consume its output choice once."); }
        bool Current() => admitted() && sourceCurrent();
        token.ThrowIfCancellationRequested();
        if (!Current())
        {
            Status = "Task output withheld: original presentation authority changed. Inspect the retained conversation; no replay.";
            return;
        }
        await present(receipt, this, Current, token).ConfigureAwait(false);
    }
}
