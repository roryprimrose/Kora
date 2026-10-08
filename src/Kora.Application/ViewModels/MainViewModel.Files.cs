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
    private LocalFileSearch? fileSearch;
    public LocalFileReview? FileReview => filePreview?.Review;
    public LocalFileRevision? FileRevision => filePreview?.Current;
    public event EventHandler? FilePreviewChanged;
    public event EventHandler? FileInspectionRequested;

    public void BindFilePreview(LocalFilePreview service, IUserFilePicker picker, LocalFileSearch? search = null)
    {
        if (filePreview is not null) { throw new InvalidOperationException("File preview is already bound."); }
        filePreview = service;
        filePicker = picker;
        fileSearch = search;
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

    public async Task ConfirmFilePreviewAsync(Guid reviewId)
    {
        var origin = HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;
        if (origin != RequestOrigin.LocalUi || filePreview is null || FileReview is not { } reviewed
            || reviewed.ReviewId != reviewId)
        {
            PresentFileOutcome(LocalFileOutcome.Stale);
            return;
        }
        var request = new HostRequest(new(Guid.NewGuid()), reviewed.Request.SessionId, reviewed.Request.TaskId, origin);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request,
            [new ActivityLink(reviewed.Cause)]);
        var callRevision = CallPolicyRevision;
        var outcome = await filePreview.ConfirmAsync(reviewId,
            () => IsClipboardEligible(RequestOrigin.LocalUi, callRevision), CancellationToken.None);
        PresentFileOutcome(outcome);
        activity.Complete(outcome == LocalFileOutcome.Admitted ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
    }

    public void ClearFilePreview() => filePreview?.Clear();

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
            if (FileRevision is null) { PresentFileOutcome(LocalFileOutcome.Stale); return; }
            FileInspectionRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        var outcome = await filePreview.SelectAsync(filePicker, Eligible, CancellationToken.None);
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
            + "Folder preview, attachments, knowledge sources, persistent/vector indexes and reasoning are unavailable.");
    }

    private void DisposeFilePreview()
    {
        if (filePreview is null) { return; }
        filePreview.Changed -= OnFilePreviewChanged;
        filePreview.Dispose();
    }
}
