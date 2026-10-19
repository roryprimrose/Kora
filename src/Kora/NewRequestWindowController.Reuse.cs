using Avalonia;
using Avalonia.Controls;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class NewRequestWindowController
{
    private OriginalMessageReuseReview? reuse;
    private HostId<SessionIdentity>? reuseSource;
    private long reuseEpoch;
    private bool reuseBusy;
    internal Func<OriginalMessageReuseReview, Window, Task<bool>>? DecideOriginalReuse { get; set; }

    internal async Task<string> ReviewOriginalReuseAsync(SessionWorkspaceEntry source, ConversationMessage original,
        Window? owner, Func<bool> admitted, CancellationToken token)
    {
        if (reuseBusy)
        {
            CancelOriginalReuse();
            return "Original USER reuse cancelled: another handoff was already open. Review again explicitly.";
        }
        if (disposed || !access.CanInspect || !access.CanControl || owner is not { IsVisible: true })
        { return "Original USER reuse unavailable: current private native New request ownership is required."; }
        var state = composer ?? create();
        var ownsPending = !ReferenceEquals(state, composer);
        reuseBusy = true;
        var epoch = Interlocked.Increment(ref reuseEpoch);
        var revision = access.ControlRevision;
        var originalWindow = window;
        var originalComposer = composer;
        var ownerAdmission = 1;
        bool Current() => !disposed && Volatile.Read(ref reuseEpoch) == epoch && access.CanInspect && access.CanControl
            && access.ControlRevision == revision && Volatile.Read(ref ownerAdmission) == 1 && admitted();
        void WithdrawOwner()
        {
            if (Interlocked.Exchange(ref ownerAdmission, 0) == 1) { CancelOriginalReuse(); }
        }
        void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Visual.IsVisibleProperty && !owner.IsVisible) { WithdrawOwner(); }
        }
        void OwnerClosed(object? sender, EventArgs args) => WithdrawOwner();
        void TargetChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (reuse is { Available: false }) { CancelOriginalReuse(); }
        }
        try
        {
            // Storage continuations may query admission off-thread; native visibility is observed only by UI events.
            owner.PropertyChanged += OwnerChanged;
            owner.Closed += OwnerClosed;
            reuseSource = source.Authority.SessionId;
            var reviewed = await OriginalMessageReuseReview.ReviewAsync(workspace, access, source, original, state,
                Current, token);
            using (reviewed)
            {
                reuse = reviewed;
                state.PropertyChanged += TargetChanged;
                var confirm = DecideOriginalReuse is { } decide
                    ? await decide(reviewed, owner)
                    : await new OriginalMessageReuseWindow(owner, reviewed).Open(owner);
                if (!confirm || !reviewed.Available || !Current()
                    || !ReferenceEquals(window, originalWindow) || !ReferenceEquals(composer, originalComposer))
                { return "Original USER reuse cancelled. Existing New request memory kept; no target, Save, Submit or replay."; }
                // Open/activate before the final authoritative reread. Native callbacks cannot publish a
                // seed from the earlier review, or replace a newer/closed window.
                if (!OpenCore(state) || !Current() || !reviewed.Available)
                { return "Original USER reuse cancelled while opening New request. No content handoff completed."; }
                return await reviewed.ConfirmAsync(replaceDirtyBuffer: true, token)
                    ? "Exact original USER text copied to independent private New request memory only. Fresh Create, review, Save and Submit remain separate; no replay."
                    : "Original USER reuse cancelled by changed source/private/buffer admission. No content handoff completed.";
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            main.ReportHostInteractionFailure("Original USER reuse refused; no replay or implicit write. Failure type: " + exception.GetType().Name);
            return "Original USER reuse unavailable or revoked. Existing independent memory kept; explicitly refresh and review the exact source again.";
        }
        finally
        {
            Interlocked.Exchange(ref ownerAdmission, 0);
            owner.PropertyChanged -= OwnerChanged;
            owner.Closed -= OwnerClosed;
            state.PropertyChanged -= TargetChanged;
            reuse?.Dispose();
            reuse = null;
            reuseSource = null;
            reuseBusy = false;
            if (ownsPending && !ReferenceEquals(composer, state)) { state.Dispose(); }
        }
    }

    private void CancelOriginalReuse()
    {
        Interlocked.Increment(ref reuseEpoch);
        reuse?.Dispose();
    }
}
