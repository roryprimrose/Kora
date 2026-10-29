using System.Collections.Immutable;
using System.Globalization;

using Kora.Application.Infrastructure;
using Kora.Core.Hosting;

namespace Kora.Application.Interaction;

public sealed partial class ExactGrantsViewModel
{
    private readonly PermissionGrantManagementService permissions;
    private readonly Func<bool> uriAdmission;
    private readonly bool managementView;
    private readonly HostId<SessionIdentity>? managementSession;
    private bool showRetainedHistory;
    private ImmutableArray<PermissionGrantRecord> permissionRecords = [];
    private ImmutableArray<PermissionGrantRow> permissionRows = [];
    private PermissionGrantRow? selectedPermission;
    private int permissionSelectionCount;
    private string grouping = "None";
    private string uriPatternInput = string.Empty;

    public static ImmutableArray<string> GroupingChoices { get; } = ["None", "Artefact type", "Artefact scope"];
    public const string GroupingDisclosure = "Grouping affects only the loaded exact-grant page and the device URI rules shown here, "
        + "not a global scan. Groups grant no selection or revocation authority. Missing target metadata is Unknown; "
        + "artefact scope (Built-in/Kora/User) is not grant lifetime (Once/Session/Perpetual). "
        + "Exact filters and Next apply only to exact grants; URI rules retain their separate matching and enforcement policy.";
    public ImmutableArray<PermissionGrantRow> PermissionRows => permissionRows;
    public bool CanGroup => !disposed && !Busy;
    public bool ShowRetainedHistory
    {
        get => showRetainedHistory;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!SetProperty(ref showRetainedHistory, value)) { return; }
            statusFilter = value || !managementView ? "Any" : "Active";
            OnPropertyChanged(nameof(StatusFilter));
            OnPropertyChanged(nameof(CanFilterStoredStatus));
            FiltersChanged();
        }
    }
    public bool CanFilterStoredStatus => !managementView || ShowRetainedHistory;
    public string PermissionContext => !managementView ? "Retained grant inventory"
        : managementSession is { } session
            ? "Always grants and grants for this exact session: " + session.Value.ToString("D")
            : "Always grants and device-wide URI rules. Open from a session to include that session's grants.";
    public AsyncCommand AddUriRuleCommand { get; }
    public AsyncCommand RemoveUriRuleCommand { get; }
    public AsyncCommand ClearUriRulesCommand { get; }
    public bool CanChangeUriRules => !disposed && !Busy && permissions.CanManageUriRules
        && access.CanControl && nativeAdmission() && uriAdmission();
    public bool CanRemoveUriRule => CanChangeUriRules && permissionSelectionCount == 1
        && SelectedPermission?.Grant is UriPermissionGrant;
    public string UriPatternInput
    {
        get => uriPatternInput;
        set { ObjectDisposedException.ThrowIf(disposed, this); SetProperty(ref uriPatternInput, value); NotifyPermissionCommands(); }
    }
    public string Grouping
    {
        get => grouping;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!GroupingChoices.Contains(value, StringComparer.Ordinal))
            { throw new ArgumentException("Select a known permission grouping.", nameof(value)); }
            if (string.Equals(grouping, value, StringComparison.Ordinal)) { return; }
            if (!CanGroup) { throw new InvalidOperationException("Wait for the current permission operation before regrouping."); }
            grouping = value;
            RetirePermissionSelection();
            RebuildPermissionRows();
            OnPropertyChanged(nameof(Grouping));
            Message = "Grouping changed for this loaded inventory only. Prior selection and confirmations retired; no read or permission change performed.";
        }
    }
    public PermissionGrantRow? SelectedPermission
    {
        get => selectedPermission;
        set
        {
            if (value is not null && !PermissionRows.Any(row => ReferenceEquals(row, value)))
            { throw new InvalidOperationException("Select a permission from the current inventory."); }
            if (!SetProperty(ref selectedPermission, value)) { return; }
            permissionSelectionCount = value is null ? 0 : 1;
            Selected = (value?.Grant as ExactOperationPermissionGrant)?.Inspection;
            SetBatchSelection([]);
            OnPropertyChanged(nameof(UriInspection));
            NotifyPermissionCommands();
        }
    }
    public string UriInspection => SelectedPermission?.Grant is UriPermissionGrant uri
        ? "URI pattern: " + uri.Pattern + Environment.NewLine
            + "Device-local address preapproval, not an exact operation grant. Its existing consumer-specific checks still apply; "
            + "this rule does not enable web access, approve redirects, bypass mandatory consent or authorize unrelated tools. "
            + "Artefact scope is Unknown because an external address has no Built-in/Kora/User artefact ownership. "
            + "Remove affects subsequent matches; it does not stop or roll back running work."
        : "Select a URI rule to inspect its exact canonical pattern. URI rules cannot join exact-grant revocation batches.";

    public void SetPermissionSelection(IEnumerable<PermissionGrantRow> selection)
    {
        var rows = selection.ToImmutableArray();
        if (rows.Distinct().Count() != rows.Length || rows.Any(row => !PermissionRows.Any(current => ReferenceEquals(current, row))))
        { throw new InvalidOperationException("Select unique permissions only from the current inventory."); }
        permissionSelectionCount = rows.Length;
        SetBatchSelection(rows.All(row => row.Grant is ExactOperationPermissionGrant)
            ? rows.Select(row => ((ExactOperationPermissionGrant)row.Grant).Inspection) : []);
        NotifyPermissionCommands();
    }

    private void RetirePermissionSelection()
    {
        selectedPermission = null;
        permissionSelectionCount = 0;
        Selected = null;
        SetBatchSelection([]);
        OnPropertyChanged(nameof(SelectedPermission));
        OnPropertyChanged(nameof(UriInspection));
    }

    private void SetPermissionRecords(ImmutableArray<PermissionGrantRecord> values)
    {
        permissionRecords = values;
        selectedPermission = null;
        permissionSelectionCount = 0;
        RebuildPermissionRows();
        OnPropertyChanged(nameof(SelectedPermission));
        OnPropertyChanged(nameof(UriInspection));
        NotifyPermissionCommands();
    }

    private void RebuildPermissionRows()
    {
        permissionRows = string.Equals(grouping, "None", StringComparison.Ordinal)
            ? permissionRecords.Select(record => new PermissionGrantRow(record, string.Empty)).ToImmutableArray()
            : permissionRecords.GroupBy(record => string.Equals(grouping, "Artefact type", StringComparison.Ordinal)
                    ? record.ArtifactType : record.ArtifactScope, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .SelectMany(group => group.Select((record, index) => new PermissionGrantRow(record,
                    index == 0 ? group.Key + " (" + group.Count().ToString(CultureInfo.InvariantCulture) + ")" : string.Empty)))
                .ToImmutableArray();
        OnPropertyChanged(nameof(PermissionRows));
    }

    public Task AddUriRuleAsync(CancellationToken token = default)
    {
        var pattern = UriPatternInput;
        return ChangeUriRulesAsync(admitted => permissions.AddUriRule(pattern, admitted), token);
    }

    public Task RemoveUriRuleAsync(CancellationToken token = default)
    {
        if (permissionSelectionCount != 1 || SelectedPermission?.Grant is not UriPermissionGrant uri)
        { throw new InvalidOperationException("Select exactly one current URI rule for removal."); }
        return ChangeUriRulesAsync(admitted => permissions.RemoveUriRule(uri, admitted), token);
    }

    public Task ClearUriRulesAsync(CancellationToken token = default) =>
        ChangeUriRulesAsync(permissions.ClearUriRules, token);

    private async Task ChangeUriRulesAsync(Action<Func<bool>> change, CancellationToken token)
    {
        if (!CanChangeUriRules) { throw new InvalidOperationException("URI permission control is unavailable."); }
        var version = selectionVersion;
        using (var linked = Begin(token))
        {
            var revision = access.ControlRevision;
            bool Admitted() => !disposed && !linked.IsCancellationRequested && access.CanControl
                && access.ControlRevision == revision && selectionVersion == version && nativeAdmission() && uriAdmission();
            try
            {
                change(Admitted);
                RequireCurrent(revision, version, controlRequired: true, linked.Token);
                UriPatternInput = string.Empty;
            }
            finally { EndOperation(); }
        }
        await RefreshAsync(token);
        Message = "URI permission rule saved and read back; inventory refreshed. Exact operation authority and running work are unchanged.";
    }

    private void NotifyPermissionCommands()
    {
        OnPropertyChanged(nameof(CanGroup));
        OnPropertyChanged(nameof(CanChangeUriRules));
        OnPropertyChanged(nameof(CanRemoveUriRule));
        AddUriRuleCommand.NotifyCanExecuteChanged();
        RemoveUriRuleCommand.NotifyCanExecuteChanged();
        ClearUriRulesCommand.NotifyCanExecuteChanged();
    }
}
