using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Body-free observations, not a plan, execution authority or a globally atomic inventory.</summary>
public sealed record SessionRemainingWorkAnswer(
    SessionWorkspaceEntry Session, DateTimeOffset DurableObservedAt, DateTimeOffset OrdinaryObservedAt,
    ImmutableArray<RemainingWorkTask> Unresolved, ImmutableArray<SessionWorkQuestion> Questions,
    int OmittedTasks, int OmittedQuestions, int OtherQueueRecords, int OmittedQueueRecords)
{
    internal long AuthorityRevision { get; init; }
    internal ImmutableArray<RemainingWorkTask> SourceTasks { get; init; } = [];
    public RemainingWorkTask? CurrentReceipt { get; init; }
    public required ConversationWorkObservation Ordinary { get; init; }
    internal DateTimeOffset ExpiresAt { get; init; }

    public string FormatSpeech()
    {
        var running = Ordinary.Running;
        var phase = running?.Phase switch
        {
            ConversationExecutionPhase.Waiting => "waiting for admitted resources",
            ConversationExecutionPhase.Running => "running",
            ConversationExecutionPhase.Cancelling => "cancelling, not joined",
            ConversationExecutionPhase.Completing => "confirming terminal storage",
            ConversationExecutionPhase.Terminal => "terminal owner observed",
            _ => "no current execution confirmed",
        };
        var step = running?.Progress is { } progress && Ordinary.ProgressCurrent
            ? progress.Step == ConversationExecutionStep.ReadingObservation ? "; reading an admitted observation" : "; generating an answer" : string.Empty;
        return (Session.Metadata?.Name.Value ?? "Unnamed conversation") + ": " + phase + step
            + "; " + Ordinary.Turns.Count(turn => turn.Phase == ConversationPendingPhase.Pending).ToString(CultureInfo.InvariantCulture)
            + " pending in FIFO, dispatch " + (Ordinary.Paused || Ordinary.LifecycleHeld ? "held" : "not observed held")
            + "; " + Questions.Length.ToString(CultureInfo.InvariantCulture) + " pending questions; "
            + Unresolved.Length.ToString(CultureInfo.InvariantCulture) + " unresolved ledger records"
            + (Unresolved.IsEmpty ? string.Empty : ", including " + string.Join(", ", Unresolved.Select(task => task.State).Distinct()))
            + ". Bounded separate observations; full positions, expiries, source times and omissions are visual; no future plan or completion inferred.";
    }

    public string Format()
    {
        var text = new StringBuilder("Conversation: ").Append(Session.Metadata?.Name.Value ?? "Unnamed conversation")
            .Append(Session.Authority.IsActive ? " (Active)." : " (Done; observed without Resume).")
            .Append("\nDurable ledger observed ").Append(Stamp(DurableObservedAt))
            .Append("; ordinary lane observed ").Append(Stamp(OrdinaryObservedAt)).Append('.');
        var running = Ordinary.Running;
        text.Append("\nCurrent ordinary work: ").Append(running is null
            ? "no in-memory execution owner observed; this does not prove completion."
            : running.Phase switch
            {
                ConversationExecutionPhase.Waiting => "waiting for admitted resources.",
                ConversationExecutionPhase.Running => "running.",
                ConversationExecutionPhase.Cancelling => "cancellation accepted; resource join still pending.",
                ConversationExecutionPhase.Completing => "terminal storage in progress; outcome not yet confirmed.",
                ConversationExecutionPhase.Terminal => "terminal owner observed; not an active execution.",
                _ => "unconfirmed; no completion inferred.",
            });
        if (running?.Progress is { } progress && Ordinary.ProgressCurrent)
        {
            text.Append("\nActual host step: ").Append(progress.Step == ConversationExecutionStep.ReadingObservation
                ? "reading an admitted observation." : "generating an answer.");
        }
        else { text.Append("\nActual host step: unavailable."); }
        if (CurrentReceipt is { } receipt) { text.Append("\nDurable current-request state: ").Append(receipt.State).Append('.'); }
        text.Append("\nOrdinary dispatch: ").Append(Ordinary.Paused || Ordinary.LifecycleHeld ? "held." : "not observed held.");
        foreach (var turn in Ordinary.Turns)
        {
            // The dispatching original and the running owner are the same request, not two remaining tasks.
            if (running?.Target.Request == turn.Request) { continue; }
            text.Append('\n').Append(turn.Phase == ConversationPendingPhase.Dispatching
                ? "Submitted request still owned by the dispatch lane (not proof of execution)"
                : "Submitted FIFO position " + turn.Position.ToString(CultureInfo.InvariantCulture))
                .Append("; ").Append(turn.ExpiresAt <= OrdinaryObservedAt ? "expired as observed" : "expires " + Stamp(turn.ExpiresAt))
                .Append(turn.Generation == Session.Authority.Generation ? "." : "; earlier session source, not renewed.");
        }
        text.Append("\nPending ordinary FIFO entries: ")
            .Append(Ordinary.Turns.Count(turn => turn.Phase == ConversationPendingPhase.Pending).ToString(CultureInfo.InvariantCulture))
            .Append(". Accepting originals not yet retained: ").Append(Ordinary.Accepting.ToString(CultureInfo.InvariantCulture)).Append('.');
        text.Append("\nPending questions: ").Append(Questions.Length.ToString(CultureInfo.InvariantCulture))
            .Append(" observed; answer only in their original question/approval surface.");
        foreach (var question in Questions)
        {
            text.Append("\nQuestion: ").Append(question.Kind).Append("; ")
                .Append(question.ExpiresAt <= DurableObservedAt ? "expired as observed" : "expires " + Stamp(question.ExpiresAt))
                .Append(question.Generation == Session.Authority.Generation ? "." : "; earlier session source.");
        }
        text.Append("\nDurable unresolved records: ").Append(Unresolved.Length.ToString(CultureInfo.InvariantCulture)).Append('.');
        foreach (var task in Unresolved)
        {
            var ordinary = running?.Target.Request == task.Request || Ordinary.Turns.Any(turn => turn.Request == task.Request);
            text.Append("\nRecorded ").Append(ordinary ? "ordinary request" : "work").Append(": ").Append(task.State)
                .Append(task.CurrentSource && task.Generation == Session.Authority.Generation ? "." : "; earlier/unavailable source.");
        }
        text.Append("\nBounds and omissions: ").Append(OmittedTasks.ToString(CultureInfo.InvariantCulture))
            .Append(" task records, ").Append(OmittedQuestions.ToString(CultureInfo.InvariantCulture))
            .Append(" pending questions omitted. Separate diagnostic queue: ").Append(OtherQueueRecords.ToString(CultureInfo.InvariantCulture))
            .Append(" observed receipts, ").Append(OmittedQueueRecords.ToString(CultureInfo.InvariantCulture))
            .Append(" older receipts omitted; not ordinary FIFO positions.");
        text.Append("\nThis is a bounded, separately timed observation, not an all-clear or atomic global plan. ")
            .Append("No future steps, percentage, ETA or effects are recorded here. Unsent text, bodies, history and other conversations are excluded. ")
            .Append("Missing workers do not erase Unknown or Interrupted work. Execution continues; nothing was submitted, resumed or changed.");
        return text.ToString();
    }

    private static string Stamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}