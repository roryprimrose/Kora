using Kora.Core.Conversation;

namespace Kora.Application.Hosting;

public sealed partial class NewRequestComposer
{
    internal long ReuseBufferEpoch => Volatile.Read(ref epoch);

    internal bool IsReuseBufferCurrent(long expected) => CanCreate && ReuseBufferEpoch == expected;

    internal bool SeedOriginal(long expected, string original, Func<bool> admitted)
    {
        if (!IsReuseBufferCurrent(expected) || !admitted()) { return false; }
        ConversationText.Validate(original, submission: true);
        var previous = body;
        var control = access.ControlRevision;
        RetireReview();
        var seededEpoch = ReuseBufferEpoch;
        body = original;
        status = "Original USER text copied by explicit confirmation into independent private memory only. "
            + "No task replay/resumption, target creation, Save or Submit. Create a fresh target and review again separately.";
        var completed = false;
        try
        {
            Notify();
            completed = IsReuseBufferCurrent(seededEpoch) && access.ControlRevision == control && admitted();
            return completed;
        }
        finally
        {
            if (!completed)
            {
                // Binding/native callbacks can revoke publication. Erase the uncompleted source handoff,
                // but preserve a newer independent user edit rather than silently restoring over it.
                body = closed || !access.CanInspect ? string.Empty
                    : ReuseBufferEpoch != seededEpoch && !string.Equals(body, original, StringComparison.Ordinal) ? body : previous;
                RetireReview();
                status = "Original-text handoff cancelled by changed private/source/buffer admission; no reuse completed.";
                Notify();
            }
        }
    }
}
