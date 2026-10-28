using System.Windows.Input;

using Kora.Application.Infrastructure;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    internal Action<HostId<SessionIdentity>, Func<bool>>? InspectPermissionGrants { get; set; }
    private AsyncCommand? permissionGrantsCommand;
    public ICommand PermissionGrantsCommand => permissionGrantsCommand ??= new AsyncCommand(() => RunAsync(() =>
    {
        var target = RequireSelected();
        var admitted = CaptureDraftPresentationEligibility();
        (InspectPermissionGrants ?? throw new InvalidOperationException("Permission grants navigation is unavailable."))
            (target.Authority.SessionId, admitted);
        return Task.CompletedTask;
    }), exception =>
    {
        Kora.Application.Diagnostics.ApplicationLog.Error(logger, exception, "Opening session permission grants");
        status = "Permission grants unavailable: " + exception.GetType().Name;
        Notify();
    });
}
