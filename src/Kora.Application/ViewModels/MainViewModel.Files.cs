using System.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Files;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private LocalFilePreview? filePreview;
    private IUserFilePicker? filePicker;
    private IUserFolderPicker? folderPicker;
    private LocalFileSearch? fileSearch;
    private LocalFileRefresh? fileRefresh;
    public LocalFileReview? FileReview => filePreview?.Review;
    public LocalFileRevision? FileRevision => filePreview?.Current;
    public LocalFolderReview? FolderReview => filePreview?.FolderReview;
    public LocalFolderRevision? FolderRevision => filePreview?.CurrentFolder;
    public event EventHandler? FilePreviewChanged;
    public event EventHandler? FileInspectionRequested;

    public void BindFilePreview(LocalFilePreview service, IUserFilePicker picker, LocalFileSearch? search = null,
        IUserFolderPicker? folders = null, LocalFileRefresh? refresh = null)
    {
        if (filePreview is not null) { throw new InvalidOperationException("File preview is already bound."); }
        filePreview = service;
        filePicker = picker;
        fileSearch = search;
        folderPicker = folders;
        fileRefresh = refresh;
        service.Changed += OnFilePreviewChanged;
    }

    private void OnFilePreviewChanged(object? sender, EventArgs args) => uiDispatcher.Post(() =>
    {
        OnPropertyChanged(nameof(IsCancelTaskVisible));
        FilePreviewChanged?.Invoke(this, EventArgs.Empty);
    });

    public Task PreviewFileAsync() => HostRequestRunner.RunAsync(HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi,
        () => RouteTranscriptAsync("preview file", 1, Kora.Core.Auditing.SecurityAuditInitiator.LocalUser,
            Interlocked.Read(ref manualCallSpeechRevision)));

    public Task PreviewFolderAsync() => HostRequestRunner.RunAsync(HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi,
        () => RouteTranscriptAsync("preview folder", 1, Kora.Core.Auditing.SecurityAuditInitiator.LocalUser,
            Interlocked.Read(ref manualCallSpeechRevision)));

    public async Task ConfirmFilePreviewAsync(Guid reviewId)
    {
        var origin = HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;
        var reviewed = FileReview;
        var folder = FolderReview;
        var original = reviewed?.Request ?? folder?.Request;
        var cause = reviewed?.Cause ?? folder?.Cause;
        if (origin != RequestOrigin.LocalUi || filePreview is null || original is null
            || (reviewed?.ReviewId ?? folder!.ReviewId) != reviewId)
        {
            PresentFileOutcome(LocalFileOutcome.Stale);
            return;
        }
        var request = new HostRequest(new(Guid.NewGuid()), original.SessionId, original.TaskId, origin);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request,
            [new ActivityLink(cause!.Value)]);
        var callRevision = CallPolicyRevision;
        var outcome = reviewed is not null
            ? await filePreview.ConfirmAsync(reviewId, () => IsClipboardEligible(RequestOrigin.LocalUi, callRevision), CancellationToken.None)
            : await filePreview.ConfirmFolderAsync(reviewId, () => IsClipboardEligible(RequestOrigin.LocalUi, callRevision), CancellationToken.None);
        PresentFileOutcome(outcome);
        activity.Complete(outcome == LocalFileOutcome.Admitted ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
    }

    public void ClearFilePreview() => filePreview?.Clear();

    public Task RefreshFilePreviewAsync(LocalFileReference exactSource)
    {
        if (FileRevision is not { } admitted || admitted.Reference != exactSource)
        {
            PresentFileOutcome(LocalFileOutcome.Stale);
            return Task.CompletedTask;
        }
        return RefreshPreviewAsync(admitted.Review.Request, admitted.Review.Cause,
            gate => fileRefresh!.ExecuteAsync(exactSource, gate, CancellationToken.None));
    }

    public Task RefreshFolderPreviewAsync(LocalFolderReference exactSource)
    {
        if (FolderRevision is not { } admitted || admitted.Reference != exactSource)
        {
            PresentFileOutcome(LocalFileOutcome.Stale);
            return Task.CompletedTask;
        }
        return RefreshPreviewAsync(admitted.Review.Request, admitted.Review.Cause,
            gate => fileRefresh!.ExecuteAsync(exactSource, gate, CancellationToken.None));
    }

    private async Task RefreshPreviewAsync(HostRequest original, ActivityContext cause,
        Func<Func<bool>, Task<LocalFileOutcome>> refresh)
    {
        if (HostActivity.HasScope && HostActivity.Current is null)
        {
            PresentFileOutcome(LocalFileOutcome.Stale);
            return;
        }
        var origin = HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;
        var callRevision = CallPolicyRevision;
        var voiceRevision = Volatile.Read(ref voiceRecoveryRevision);
        bool Eligible() => IsClipboardEligible(origin, callRevision)
            && (origin != RequestOrigin.ActivatedVoice || (IsVoiceEnabled && HasVoiceConsent
                && Volatile.Read(ref voiceRecoveryRevision) == voiceRevision));
        if (fileRefresh is null || !Eligible())
        {
            PresentFileOutcome(LocalFileOutcome.Stale);
            return;
        }
        // The host-held admission, not command text or incoming trace headers, selects this continuation.
        var request = new HostRequest(new(Guid.NewGuid()), original.SessionId, original.TaskId, origin);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request,
            [new ActivityLink(cause)]);
        ShowInformation("Explicit local preview refresh requested",
            "Starting a fresh metadata review of the exact admitted physical source. The old immutable preview and citations "
            + "are retired when this operation starts; no content is read until a separate new native confirmation. "
            + "If refresh fails or is cancelled, use the native picker for a fresh review. Original files are never modified.");
        var outcome = await refresh(Eligible);
        PresentFileOutcome(outcome);
        activity.Complete(outcome == LocalFileOutcome.Reviewed ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
    }

    public async Task<LocalFileSearchResult> SearchFileAsync(LocalFileReference exactSource, string query)
    {
        var origin = HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;
        var callRevision = CallPolicyRevision;
        if (origin != RequestOrigin.LocalUi || fileSearch is null || FileRevision is not { } admitted
            || admitted.Reference != exactSource || !IsClipboardEligible(origin, callRevision))
        {
            return LocalFileSearchResult.Empty(LocalFileSearchOutcome.Stale, DateTimeOffset.UtcNow);
        }
        var request = new HostRequest(new(Guid.NewGuid()), admitted.Review.Request.SessionId, admitted.Review.Request.TaskId, origin);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request,
            [new ActivityLink(admitted.Review.Cause)]);
        var result = await fileSearch.ExecuteAsync(exactSource, query,
            () => IsClipboardEligible(origin, callRevision), CancellationToken.None);
        // Native-only text input bypasses transcripts, speech, history and model routing.
        result = FileRevision?.Reference == exactSource && IsClipboardEligible(origin, callRevision)
            ? result : LocalFileSearchResult.Empty(LocalFileSearchOutcome.Stale, result.ObservedAt);
        activity.Complete(result.Outcome is LocalFileSearchOutcome.Matched or LocalFileSearchOutcome.NoMatch
            ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        return result;
    }

    public async Task<LocalFileSearchResult> SearchFolderAsync(LocalFolderReference exactSource, string query)
    {
        var origin = HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;
        var callRevision = CallPolicyRevision;
        if (origin != RequestOrigin.LocalUi || fileSearch is null || FolderRevision is not { } admitted
            || admitted.Reference != exactSource || !IsClipboardEligible(origin, callRevision))
        {
            return LocalFileSearchResult.Empty(LocalFileSearchOutcome.Stale, DateTimeOffset.UtcNow);
        }
        var request = new HostRequest(new(Guid.NewGuid()), admitted.Review.Request.SessionId, admitted.Review.Request.TaskId, origin);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request,
            [new ActivityLink(admitted.Review.Cause)]);
        var result = await fileSearch.ExecuteAsync(exactSource, query,
            () => IsClipboardEligible(origin, callRevision), CancellationToken.None);
        result = FolderRevision?.Reference == exactSource && IsClipboardEligible(origin, callRevision)
            ? result : LocalFileSearchResult.Empty(LocalFileSearchOutcome.Stale, result.ObservedAt);
        activity.Complete(result.Outcome is LocalFileSearchOutcome.Matched or LocalFileSearchOutcome.NoMatch
            ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        return result;
    }

    private async Task ExecuteFileCommandAsync(LocalFileCommand command)
    {
        var origin = HostActivity.RequireCurrent().Request.Origin;
        var callRevision = CallPolicyRevision;
        var voiceRevision = Volatile.Read(ref voiceRecoveryRevision);
        bool Eligible() => IsClipboardEligible(origin, callRevision)
            && (origin != RequestOrigin.ActivatedVoice || (IsVoiceEnabled && HasVoiceConsent
                && Volatile.Read(ref voiceRecoveryRevision) == voiceRevision));
        if (filePreview is null || filePicker is null || !Eligible())
        {
            PresentFileOutcome(LocalFileOutcome.Denied);
            return;
        }
        if (command.Operation == LocalFileOperation.Invalid)
        {
            PresentFileOutcome(LocalFileOutcome.Denied);
            return;
        }
        if (command.Operation == LocalFileOperation.Clear)
        {
            filePreview.Clear();
            PresentFileOutcome(LocalFileOutcome.Cleared);
            return;
        }
        if (command.Operation == LocalFileOperation.Inspect)
        {
            if (FileRevision is null && FolderRevision is null) { PresentFileOutcome(LocalFileOutcome.Stale); return; }
            FileInspectionRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (command.Operation == LocalFileOperation.Refresh)
        {
            if (FileRevision is not { } exact) { PresentFileOutcome(LocalFileOutcome.Stale); return; }
            await RefreshFilePreviewAsync(exact.Reference);
            return;
        }
        if (command.Operation == LocalFileOperation.RefreshFolder)
        {
            if (FolderRevision is not { } exact) { PresentFileOutcome(LocalFileOutcome.Stale); return; }
            await RefreshFolderPreviewAsync(exact.Reference);
            return;
        }
        if (command.Operation == LocalFileOperation.SelectFolder && folderPicker is null)
        {
            PresentFileOutcome(LocalFileOutcome.Unavailable);
            return;
        }
        var outcome = command.Operation == LocalFileOperation.SelectFolder
            ? await filePreview.SelectFolderAsync(folderPicker!, Eligible, CancellationToken.None)
            : await filePreview.SelectAsync(filePicker, Eligible, CancellationToken.None);
        PresentFileOutcome(outcome);
    }

    private void PresentFileOutcome(LocalFileOutcome outcome)
    {
        if (disposed || !IsHostInputEligible) { return; }
        ShowInformation("Local file preview: " + outcome,
            "Select one fixed-drive UTF-8 .txt, .md or .markdown file (256 KiB maximum). "
            + "The native metadata review requires exact confirmation before content is read. "
            + "Paths supplied in speech/text are proposals only: use the native picker. "
            + "Untrusted plain text may contain secrets; no rendering, execution, clipboard, model, egress, persistence or automatic refresh. "
            + "A failed selection admits nothing: resolve access/policy and select a fresh supported file. Unverified native release blocks clean handoff/exit and requires restart. "
            + "Close, clear, cancel, privacy closure or ownership loss discards the preview. "
            + "Search/inspect file opens bounded native lexical search of this exact admitted revision only. "
            + $"Preview folder reviews all immediate files only: {LocalFolderPolicy.MaximumFiles} files / "
            + $"{LocalFolderPolicy.MaximumCombinedBytes} combined bytes maximum; 256 KiB per file. "
            + "Any subdirectory, unsupported file or failed item rejects the whole selection. Search/inspect folder focuses native lexical search. "
            + "Refresh file/folder reopens only the exact admitted physical source for a fresh metadata review; confirm again before reads. "
            + "Starting refresh retires the old preview and citations. Failure/cancel leaves no preview; use the native picker again. "
            + "Missing/replaced or aliased original roots cannot be rebound by refresh. Original files are never modified. "
            + "Durable attachments, knowledge sources, persistent/vector indexes and reasoning are unavailable.");
    }

    private void DisposeFilePreview()
    {
        if (filePreview is null) { return; }
        filePreview.Changed -= OnFilePreviewChanged;
        filePreview.Dispose();
    }
}
