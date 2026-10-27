using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    private NamedConversationChoiceWindow? namedConversationChoice;
    private long namedConversationEpoch;
    internal TimeProvider NamedConversationTime { get; set; } = TimeProvider.System;
    internal Task NamedConversationOperation { get; private set; } = Task.CompletedTask;

    internal Task OpenNamedConversationAsync(SessionName name, RequestOrigin origin, Func<bool> admitted)
    {
        if (!NamedConversationOperation.IsCompleted)
        { throw new InvalidOperationException("The original exact conversation opening has not joined."); }
        return NamedConversationOperation = OpenNamedConversationCoreAsync(name, origin, admitted);
    }

    private async Task OpenNamedConversationCoreAsync(SessionName name, RequestOrigin origin, Func<bool> admitted)
    {
        if (namedConversationChoice is not null)
        { throw new InvalidOperationException("One exact conversation choice already owns navigation."); }
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("Conversation opening requires an original typed or activated-voice command."); }
        EnsurePresentation();
        var state = model!;
        var selection = state.CaptureNavigationEligibility();
        var control = access.ControlRevision;
        var epoch = namedConversationEpoch;
        var revoked = 0;
        var expires = NamedConversationTime.GetUtcNow().AddSeconds(60);
        bool Live() => Volatile.Read(ref revoked) == 0 && !disposed && admitted() && access.CanInspect && access.ControlRevision == control
            && main.CanRevealPrivatePresentation && ReferenceEquals(model, state) && selection()
            && epoch == namedConversationEpoch && NamedConversationTime.GetUtcNow() < expires;
        if (!Live()) { throw new OperationCanceledException("Original conversation navigation admission changed."); }
        var page = await service.SearchListAsync(SessionListSearchKind.ExactName, SessionListFilter.All,
            name.Value, null, 25, CancellationToken.None);
        if (!Live()) { throw new OperationCanceledException("Original conversation lookup expired or changed."); }
        if (page.Records.IsEmpty)
        {
            main.ReportHostInteractionFailure("No exact readable-name match on this bounded metadata search page. Open Sessions to search further; nothing was created or submitted.");
            return;
        }
        var completed = new TaskCompletionSource<SessionWorkspaceEntry?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var choice = new NamedConversationChoiceWindow(page, Live);
        namedConversationChoice = choice;
        choice.Closed += (_, _) => completed.TrySetResult(choice.Result);
        void Revoke(HostId<SessionIdentity> _)
        {
            Interlocked.Exchange(ref revoked, 1);
            Dispatcher.UIThread.Post(choice.Withdraw);
        }
        service.SessionMetadataChanging += Revoke;
        service.SessionLifecycleChanged += Revoke;
        service.SessionRetired += Revoke;
        try
        {
            choice.Show();
#pragma warning disable VSTHRD003 // The exact native acknowledgment completion; no blocking dispatcher wait.
            var target = await completed.Task;
#pragma warning restore VSTHRD003
            if (target is null) { return; }
            if (!Live() || !page.Records.Any(entry => ReferenceEquals(entry, target)))
            { throw new OperationCanceledException("The exact presented conversation choice was retired."); }
            await state.PresentNamedConversationAsync(target, Live, origin, CancellationToken.None);
            await ShowInteractionAsync(true, admitted);
            var surface = interaction?.SessionView;
            if (!disposed && admitted() && state.SelectedSessionRecord == target && surface is not null)
            {
                var history = surface.FindControl<TabControl>("ConversationTabs")!;
                history.SelectedIndex = 0;
                history.Focusable = true;
                history.Focus();
            }

        }
        finally
        {
            service.SessionMetadataChanging -= Revoke;
            service.SessionLifecycleChanged -= Revoke;
            service.SessionRetired -= Revoke;
            namedConversationChoice = null;
            choice.Withdraw();
        }
    }

    private void RetireNamedConversation()
    {
        namedConversationEpoch++;
        namedConversationChoice?.Withdraw();
    }
}
