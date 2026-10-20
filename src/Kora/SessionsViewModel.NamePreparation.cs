using System.Diagnostics;
using System.Globalization;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private NamePreparation? namePreparation;
    private long namePreparationRevision;
    private long nameEditorRevision;

    private sealed record NamePreparation(SessionWorkspaceEntry Target, SessionName Proposed, long SourceRevision,
        Func<bool> Current);

    public bool HasNamePreparation => namePreparation is { } candidate && candidate.Current();
    public bool CanRenamePreparedName => CanRead && HasNamePreparation;
    public string NamePreparationReview => HasNamePreparation && namePreparation is { } candidate
        ? "Exact session ID: " + candidate.Target.Authority.SessionId.Value.ToString("D")
            + "\nState: " + (candidate.Target.Authority.IsActive ? "Active" : "Done")
            + "\nGeneration: " + candidate.Target.Authority.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + "\nMetadata revision: " + (candidate.Target.Metadata?.Revision.Value ?? 0).ToString(CultureInfo.InvariantCulture)
            + "\nOld literal name: " + (candidate.Target.Metadata is { } metadata ? "\"" + metadata.Name.Value + "\"" : "(unnamed; no metadata)")
            + "\nNew literal name: \"" + candidate.Proposed.Value + "\""
            + "\nNames are labels, never selection authority. Both editor and composer remain independent. "
            + "Preparation changes nothing durable. Rename exact prepared ID is a separate explicit native gesture."
        : string.Empty;

    internal async Task<bool> PrepareNameAsync(SessionNamePreparationCommand command, RequestOrigin origin,
        Func<bool> admitted, Func<bool> nativeAdmission, CancellationToken token)
    {
        CancelNamePreparation();
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || !command.IsValid || command.IsHelp
            || !CanRead || draftRecoveryCancellation is not null)
        {
            throw new InvalidOperationException("Finish the current native operation before preparing an exact name.");
        }
        var revision = namePreparationRevision;
        var selection = selectionEpoch;
        var control = access.ControlRevision;
        var editor = nameEditorRevision;
        bool Initial() => !closed && admitted() && nativeAdmission() && access.CanInspect && access.CanControl
            && access.ControlRevision == control && selectionEpoch == selection && nameEditorRevision == editor
            && namePreparationRevision == revision && !token.IsCancellationRequested;
        var displayed = false;
        await RunNamePreparationAsync(async () =>
        {
            if (!Initial()) { throw new OperationCanceledException("Original name preparation was revoked."); }
            if (!await ProtectComposerAsync()) { return; }
            var buffer = composerBufferRevision;
            bool Current() => Initial() && composerBufferRevision == buffer;
            if (!Current()) { throw new OperationCanceledException("Original navigation changed."); }
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
            var observed = await service.ReadNamePreparationAsync(new(command.SessionId), cancellation.Token);
            if (!Current()) { throw new OperationCanceledException("The exact name source changed."); }
            namePreparation = new(observed.Entry, command.Name!, observed.SourceRevision, Current);
            status = "Name PREPARED / REVIEW NEEDED. Review the complete literal names; Rename is separate. Existing drafts were not replaced.";
            displayed = true;
        }, new(command.SessionId), origin);
        return displayed && HasNamePreparation;
    }

    public void CancelNamePreparation()
    {
        namePreparationRevision++;
        namePreparation = null;
        NotifyNamePreparation();
    }

    public Task RenamePreparedNameAsync()
    {
        var candidate = namePreparation;
        return RunNamePreparationAsync(async () =>
        {
            if (candidate is null || !candidate.Current())
            { throw new InvalidOperationException("The original name review is stale. Prepare again."); }
            var observed = await service.ReadNamePreparationAsync(candidate.Target.Authority.SessionId, lifetime.Token);
            if (!candidate.Current() || observed.Entry != candidate.Target || observed.SourceRevision != candidate.SourceRevision)
            { throw new OperationCanceledException("The exact original metadata or source changed. Prepare again."); }
            // Consume this native review once, but retain its admission fence through the authoritative commit.
            namePreparation = null;
            var renamed = await RenameAsync(candidate.Target, candidate.Proposed, candidate.Current, candidate.SourceRevision);
            var readback = await service.ReadNamePreparationAsync(candidate.Target.Authority.SessionId, lifetime.Token);
            if (!candidate.Current() || readback.Entry != renamed)
            { throw new InvalidOperationException("Rename receipt/readback is unconfirmed. Inspect durable state; do not retry automatically."); }
            status = "Committed explicit rename for the exact prepared ID. Lifecycle and activity unchanged; existing drafts were not replaced.";
        }, candidate?.Target.Authority.SessionId ?? viewer.SessionId, RequestOrigin.LocalUi);
    }

    private Task<SessionWorkspaceEntry> RenameAsync(SessionWorkspaceEntry target, SessionName proposed, Func<bool>? admitted = null,
        long? expectedSourceRevision = null) =>
        service.RenameAsync(target.Authority.SessionId, target.Authority.Generation, target.Metadata?.Revision.Value ?? 0,
            proposed, RequestOrigin.LocalUi, lifetime.Token, admitted, expectedSourceRevision);

    private async Task RunNamePreparationAsync(Func<Task> operation, HostId<SessionIdentity> subject, RequestOrigin origin)
    {
        if (!CanRead) { return; }
        ActivityLink[] links = HostActivity.Current?.Activity is { } parent ? [new(parent.Context)] : [];
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), subject, viewer.TaskId, origin),
            HostActivityLayer.Desktop, HostOperation.Presentation, links);
        busy = true;
        Notify();
        try
        {
            await operation();
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException or System.Data.Common.DbException)
        {
            CancelNamePreparation();
            Failure(logger, exception.GetType().Name);
            status = "Name review unavailable/unconfirmed: " + exception.GetType().Name
                + ". Existing drafts retained. Inspect exact durable metadata before preparing again; no rollback or automatic retry is claimed.";
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            busy = false;
            Notify();
            if (closed) { lifetime.Dispose(); }
        }
    }

    private void NotifyNamePreparation()
    {
        if (namePreparation is { } candidate && !candidate.Current()) { namePreparation = null; }
        OnPropertyChanged(nameof(HasNamePreparation));
        OnPropertyChanged(nameof(CanRenamePreparedName));
        OnPropertyChanged(nameof(NamePreparationReview));
    }
}
