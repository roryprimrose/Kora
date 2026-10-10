using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using Kora.Application.Infrastructure;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Interaction;

public sealed partial class ExactGrantsViewModel : ObservableObject, IDisposable
{
    public const string Disclosure = "Exact host-issued operation grants, not legacy named-action preferences. "
        + "Stored Active is not an allow decision. Current applicability and running-work status are unknown here. "
        + "Revocation denies subsequent grant consumes; it does not stop or roll back already-running effects. "
        + "No execution, model calls, bulk removal, edits or new approvals are available.";
    private readonly IExactGrantStore store;
    private readonly ExactGrantControlAdmission control;
    private readonly ISessionWorkspaceAccess access;
    private readonly Func<bool> nativeAdmission;
    private readonly ILogger<ExactGrantsViewModel> logger;
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    private bool busy;
    private bool reviewed;
    private long selectionVersion;
    private long previewControlRevision;
    private ImmutableArray<ExactGrantInspection> records = [];
    private ExactGrantCursor? next;
    private ExactGrantInspection? selected;
    private ExactGrantInspection? preview;
    private string message = "Refresh to read bounded retained metadata from the current private store.";

    public ExactGrantsViewModel(IExactGrantStore store, ExactGrantControlAdmission control,
        ISessionWorkspaceAccess access, Func<bool> nativeAdmission, ILogger<ExactGrantsViewModel> logger)
    {
        this.store = store;
        this.control = control;
        this.access = access;
        this.nativeAdmission = nativeAdmission;
        this.logger = logger;
        RefreshCommand = new(() => RefreshAsync(), ReportFailure, () => !Busy && !disposed);
        NextCommand = new(() => NextAsync(), ReportFailure, () => !Busy && next is not null && !disposed);
        InspectCommand = new(() => InspectAsync(), ReportFailure, () => !Busy && Selected is not null && !disposed);
        RevokeCommand = new(() => RevokeAsync(), ReportFailure, () => CanRevoke);
    }

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand NextCommand { get; }
    public AsyncCommand InspectCommand { get; }
    public AsyncCommand RevokeCommand { get; }
    public ImmutableArray<ExactGrantInspection> Records { get => records; private set => SetProperty(ref records, value); }
    public string Message { get => message; private set => SetProperty(ref message, value); }
    public bool Busy { get => busy; private set { SetProperty(ref busy, value); NotifyCommands(); } }
    public bool Reviewed { get => reviewed; set { SetProperty(ref reviewed, value); NotifyCommands(); } }
    public bool CanRevoke => !disposed && !Busy && Reviewed && preview?.Grant.Status == OperationGrantStatus.Active
        && access.CanControl && nativeAdmission() && previewControlRevision == access.ControlRevision;
    public ExactGrantInspection? Selected
    {
        get => selected;
        set
        {
            if (!SetProperty(ref selected, value)) { return; }
            selectionVersion++;
            preview = null;
            Reviewed = false;
            OnPropertyChanged(nameof(Inspection));
            NotifyCommands();
        }
    }
    public string Inspection => preview is null ? "Select one exact retained ID, then Inspect current record."
        : Format(preview);

    public Task RefreshAsync(CancellationToken token = default) => ReadPageAsync(null, token);
    public Task NextAsync(CancellationToken token = default) =>
        ReadPageAsync(next ?? throw new InvalidOperationException("No bounded continuation is available."), token);

    private async Task ReadPageAsync(ExactGrantCursor? cursor, CancellationToken token)
    {
        using var linked = Begin(token);
        var revision = access.ControlRevision;
        Selected = null;
        var version = ++selectionVersion;
        preview = null;
        Reviewed = false;
        Records = [];
        next = null;
        try
        {
            var page = await store.ReadExactGrantPageAsync(cursor, linked.Token);
            RequireCurrent(revision, version, controlRequired: false, linked.Token);
            Records = page.Records;
            next = page.Next;
            Message = page.Next is null ? "Current bounded page. No more records in this snapshot."
                : "Current bounded page. Next reads another page from the same unchanged snapshot.";
        }
        finally { Busy = false; }
    }

    public async Task InspectAsync(CancellationToken token = default)
    {
        var target = Selected ?? throw new InvalidOperationException("Select one exact ID first.");
        using var linked = Begin(token);
        var revision = access.ControlRevision;
        var version = selectionVersion;
        preview = null;
        Reviewed = false;
        OnPropertyChanged(nameof(Inspection));
        try
        {
            var current = await store.InspectExactGrantAsync(target.Grant.Id, linked.Token);
            RequireCurrent(revision, version, controlRequired: false, linked.Token);
            preview = current;
            previewControlRevision = revision;
            Message = current is null ? "Exact retained record is unavailable. No applicability or revocation is inferred."
                : "Current exact inspection. Review this immutable ID/revision before explicit confirmation.";
            OnPropertyChanged(nameof(Inspection));
        }
        finally { Busy = false; }
    }

    public async Task RevokeAsync(CancellationToken token = default)
    {
        if (!CanRevoke) { throw new InvalidOperationException("Current exact active inspection and explicit native confirmation are required."); }
        using var linked = Begin(token);
        var exact = preview!;
        var revision = previewControlRevision;
        var version = selectionVersion;
        bool Admitted() => !disposed && nativeAdmission() && access.CanControl && access.ControlRevision == revision
            && selectionVersion == version && ReferenceEquals(preview, exact) && Reviewed;
        try
        {
            var decision = await control.RevokeAsync(exact, Admitted, linked.Token);
            RequireCurrent(revision, version, controlRequired: true, linked.Token);
            preview = null;
            Reviewed = false;
            Message = decision.Outcome == HostInteractionOutcome.Revoked
                ? "Exact revocation committed and read back. Subsequent consumes denied. Already-running work: unknown; not stopped or rolled back. Refresh to inspect retained state."
                : "Exact preview is stale, missing or already terminal. No new revocation applied. Refresh and inspect current authority.";
            OnPropertyChanged(nameof(Inspection));
        }
        finally { Busy = false; }
    }

    private CancellationTokenSource Begin(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Busy || !access.CanInspect || !nativeAdmission())
        {
            throw new InvalidOperationException("Exact-grant ownership/privacy inspection is unavailable or busy.");
        }
        token.ThrowIfCancellationRequested();
        Busy = true;
        return CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
    }

    private void RequireCurrent(long revision, long version, bool controlRequired, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!nativeAdmission() || !access.CanInspect || access.ControlRevision != revision
            || version != selectionVersion || controlRequired && !access.CanControl)
        {
            throw new InvalidOperationException("Exact selection, ownership, privacy or control lifetime changed; no late output is presented.");
        }
    }

    public void ReportFailure(Exception exception)
    {
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Recovery, RequestOrigin.LocalUi);
        Failed(exception.GetType().Name);
        activity.Complete(HostOperationOutcome.Failed);
        if (disposed) { return; }
        preview = null;
        Reviewed = false;
        Records = [];
        next = null;
        Message = "Exact-grant operation unavailable. No success or rollback is claimed. Close and reopen, refresh and inspect current authority before any retry. Failure type: " + exception.GetType().Name;
        OnPropertyChanged(nameof(Inspection));
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        InspectCommand.NotifyCanExecuteChanged();
        RevokeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRevoke));
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        selectionVersion++;
        lifetime.Cancel();
        lifetime.Dispose();
        selected = preview = null;
        Records = [];
        Reviewed = false;
        Message = "Closed. No late private output is admitted.";
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(Inspection));
    }

    private static string Format(ExactGrantInspection item)
    {
        var grant = item.Grant;
        grant.Validate();
        var proposal = grant.ApprovedProposal;
        var binding = proposal.Binding;
        var text = new StringBuilder()
            .AppendLine("Approval ID: " + grant.Id.Value.ToString("D") + "; revision: " + grant.Revision.Value.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Stored status: " + grant.Status + "; scope: " + grant.Scope)
            .AppendLine("Capability/action: " + binding.ActionId + "; source: " + binding.SourcePartition + "; skill: " + binding.SkillId)
            .AppendLine("Stored effect classification: " + proposal.Effect + " (not an effect receipt)")
            .AppendLine("Original session: " + proposal.Request.SessionId.Value.ToString("D") + "; approved generation: " + grant.SessionGeneration.Value.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Origin session observation: " + (item.OriginSession is null ? "unavailable"
                : item.OriginSessionRemoved ? "removed" : item.OriginSession.IsActive ? "Active" : "Done")
                + "; current retained generation: " + (item.OriginSession?.Generation.Value.ToString(CultureInfo.InvariantCulture) ?? "unavailable"))
            .AppendLine("Creator channel: " + grant.CreatorChannel + "; created: " + grant.CreatedAt.ToString("O", CultureInfo.InvariantCulture))
            .AppendLine("Use count: " + grant.UseCount.ToString(CultureInfo.InvariantCulture)
                + "; last use: " + (grant.LastUsedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "not recorded"))
            .AppendLine("Revocation reason: " + (grant.RevocationReason ?? "not recorded"))
            .AppendLine("Policy revision: " + binding.PolicyRevision.Value.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Definition SHA-256: " + binding.DefinitionDigest)
            .AppendLine("Declared resources SHA-256: " + binding.DeclaredResourceDigest)
            .AppendLine("Tracked content SHA-256: " + binding.TrackedContentDigest)
            .AppendLine("Implementation SHA-256: " + binding.ImplementationDigest)
            .AppendLine("Invocation SHA-256: " + binding.InvocationDigest)
            .AppendLine("Resolved resources SHA-256: " + binding.ResourceDigest)
            .AppendLine("Identity SHA-256: " + binding.IdentityDigest)
            .AppendLine("Destination SHA-256: " + binding.DestinationDigest)
            .AppendLine("Transformation SHA-256: " + binding.TransformationDigest)
            .AppendLine("Original proposal ID: " + proposal.ProposalId.Value.ToString("D") + "; revision: " + proposal.Revision.Value.ToString(CultureInfo.InvariantCulture))
            .AppendLine("Original proposal deadline (not Perpetual expiry): " + proposal.ExpiresAt.ToString("O", CultureInfo.InvariantCulture))
            .Append(Disclosure).ToString();
        return text;
    }
}
