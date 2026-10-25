using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    public string InteractionTarget => selected is { } target
        ? target.Metadata?.Name.Value ?? "Unnamed conversation"
        : "Select a conversation";

    internal bool HasSessionListPage => sessions is not null;

    internal void ReportNavigationStatus(string reason)
    {
        status = reason;
        Notify();
    }

    internal Func<bool> CapturePresentedRow(SessionWorkspaceEntry row)
    {
        var page = sessions;
        var control = access.ControlRevision;
        var epoch = listSearchEpoch;
        var filter = sessionFilter;
        var order = listPageOrderRevision;
        return () => !closed && access.CanInspect && control == access.ControlRevision
            && ReferenceEquals(sessions, page) && epoch == listSearchEpoch
            && filter == sessionFilter && order == listPageOrderRevision
            && Sessions.Any(entry => ReferenceEquals(entry, row));
    }
}
