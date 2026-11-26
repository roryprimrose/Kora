using Kora.Core.Authorization.SessionPermission;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionPermissionPolicy? permissionPolicy;
    private Func<Kora.Core.Storage.SessionWorkspaceEntry?>? permissionControlTargetReader;
    private SessionPermissionLevel? reviewedPermission;
    private long permissionEpoch;
    private SessionPermissionLevel permissionChoice;

    public IReadOnlyList<string> PermissionChoices { get; } = ["Review all", "Model review", "Approve all"];
    public string CurrentSessionPermission => !closed && access.CanInspect && PermissionCurrent
        ? "Permission level: " + permissionPolicy!.DisplayName
        : "Permission level unavailable for the current session";
    public int PermissionChoice
    {
        get => (int)permissionChoice;
        set
        {
            if (!Enum.IsDefined((SessionPermissionLevel)value))
            { throw new ArgumentOutOfRangeException(nameof(value)); }
            permissionChoice = (SessionPermissionLevel)value;
            reviewedPermission = null;
            NotifySessionPermission();
        }
    }
    private bool PermissionCurrent => permissionPolicy is { } policy && selected is { } entry
        && policy.SessionId == entry.Authority.SessionId && policy.Generation == entry.Authority.Generation
        && permissionEpoch == selectionEpoch;
    public bool CanReadPermission => CanRead && selected is not null;
    public bool CanReviewPermission => CanReadPermission && access.CanControl
        && selected?.Authority.IsActive == true && PermissionCurrent;
    public bool CanConfirmPermission => CanReviewPermission && reviewedPermission is not null;
    public string PermissionStatus => !closed && access.CanInspect && PermissionCurrent
        ? $"Session {selected!.Metadata?.Name.Value ?? "Unnamed"} ({permissionPolicy!.SessionId.Value:D}): {permissionPolicy.DisplayName}. "
            + (reviewedPermission is { } desired
                ? $"Review change from {permissionPolicy.DisplayName} to {PermissionChoices[(int)desired]}. "
                    + (desired == SessionPermissionLevel.ApproveAll ? SessionPermissionPolicy.ApproveAllWarning
                        : desired == SessionPermissionLevel.ModelReview
                            ? "A separate tools-disabled reviewer is required. Unavailable or uncertain review requires your approval."
                            : "Otherwise-ungranted actions require your approval.")
                    + " Confirm explicitly or cancel. This creates no grant and executes nothing."
                : "Kora-owned reviews follow this level. Device/provider consent and mandatory protections remain. Review a change before applying.")
        : "Session action policy unavailable/stale. Read the exact selected session policy before review; no inferred default.";

    public Task ReadPermissionAsync() => RunAsync(async () =>
    {
        ClearSessionPermission();
        await ReadSelectedPermissionAsync();
    });

    private async Task ReadSelectedPermissionAsync()
    {
        if (!service.SupportsSessionPermission) { return; }
        if (permissionControlTargetReader is null)
        {
            permissionControlTargetReader = () => !closed && access.CanInspect && access.CanControl
                && PermissionCurrent && selected?.Authority.IsActive == true ? selected : null;
            service.SessionPermissionChanged += OnSessionPermissionChanged;
        }
        service.BindPermissionControlTarget(permissionControlTargetReader);
        var target = RequireSelected().Authority;
        var epoch = selectionEpoch;
        var result = await service.ReadSessionPermissionAsync(target.SessionId, lifetime.Token);
        if (closed || !access.CanInspect || selectionEpoch != epoch || selected?.Authority != target) { return; }
        result.Validate(target);
        permissionPolicy = result;
        permissionEpoch = epoch;
        permissionChoice = result.Level;
        NotifySessionPermission();
    }

    public void ReviewPermission()
    {
        if (!CanReviewPermission) { return; }
        reviewedPermission = permissionChoice;
        NotifySessionPermission();
    }

    public Task ReviewPermissionAsync()
    {
        ReviewPermission();
        return PermissionCurrent && permissionPolicy!.Level == SessionPermissionLevel.ApproveAll
            && reviewedPermission is not null ? ConfirmPermissionAsync() : Task.CompletedTask;
    }

    public Task ConfirmPermissionAsync() => RunAsync(async () =>
    {
        if (!PermissionCurrent || reviewedPermission is null || !access.CanControl || selected?.Authority.IsActive != true)
        { throw new InvalidOperationException("Review the current exact Active session policy before confirming."); }
        var expected = permissionPolicy!;
        var desired = reviewedPermission!.Value;
        var epoch = permissionEpoch;
        var control = access.ControlRevision;
        reviewedPermission = null;
        var committed = await service.SetSessionPermissionAsync(expected, desired,
            () => !closed && access.CanInspect && access.CanControl && access.ControlRevision == control
                && selectionEpoch == epoch && selected?.Authority.SessionId == expected.SessionId
                && selected.Authority.Generation == expected.Generation, lifetime.Token);
        if (closed || !access.CanInspect || selectionEpoch != epoch) { return; }
        permissionPolicy = committed;
        NotifySessionPermission();
    });

    public void CancelPermissionReview()
    {
        reviewedPermission = null;
        NotifySessionPermission();
    }

    private void ClearSessionPermission()
    {
        permissionPolicy = null;
        reviewedPermission = null;
    }

    private void OnSessionPermissionChanged(SessionPermissionPolicy policy) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (closed || !access.CanInspect || selected is not { } entry
                || entry.Authority.SessionId != policy.SessionId || entry.Authority.Generation != policy.Generation
                || PermissionCurrent && permissionPolicy!.Revision.Value >= policy.Revision.Value)
            { return; }
            policy.Validate(entry.Authority);
            permissionPolicy = policy;
            permissionEpoch = selectionEpoch;
            permissionChoice = policy.Level;
            reviewedPermission = null;
            NotifySessionPermission();
        });

    private void NotifySessionPermission()
    {
        OnPropertyChanged(nameof(CurrentSessionPermission));
        OnPropertyChanged(nameof(PermissionStatus));
        OnPropertyChanged(nameof(PermissionChoice));
        OnPropertyChanged(nameof(CanReadPermission));
        OnPropertyChanged(nameof(CanReviewPermission));
        OnPropertyChanged(nameof(CanConfirmPermission));
    }
}
