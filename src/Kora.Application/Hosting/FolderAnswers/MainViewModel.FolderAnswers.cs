using System.Diagnostics;
using Kora.Core.Context;
using Kora.Core.Context.FolderAnswers;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Files.FolderAnswers;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private LocalFolderAnswer? folderAnswer;

    public void BindFolderAnswer(LocalFolderAnswer action)
    {
        if (folderAnswer is not null) { throw new InvalidOperationException("Folder answer action is already bound."); }
        folderAnswer = action;
    }

    public async Task<FolderAnswerPreparation> PrepareFolderAnswerAsync(LocalFolderReference exactSource, string question,
        Func<bool> nativeCurrent, CancellationToken token)
    {
        var origin = HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;
        var callRevision = CallPolicyRevision;
        bool Current() => nativeCurrent() && IsClipboardEligible(origin, callRevision);
        if (!FolderAnswerEligible(exactSource, origin, Current) || FolderRevision is not { } source)
        {
            return new(LocalFileAnswerOutcome.Denied);
        }
        var input = new HostRequest(new(Guid.NewGuid()), source.Review.Request.SessionId, source.Review.Request.TaskId, origin);
        var target = LocalContextSessionTarget.ReviewedFresh(input, HostRequest.Create(origin));
        using var activity = HostActivity.BeginRoot(input, HostActivityLayer.Application, HostOperation.Request,
            [new ActivityLink(source.Review.Cause)]);
        var result = await folderAnswer!.PrepareAsync(exactSource, question, target, Current, token);
        if (!Current() || FolderRevision?.Reference != exactSource) { result = new(LocalFileAnswerOutcome.Stale); }
        activity.Complete(result.Outcome is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.NoEvidence
            ? HostOperationOutcome.Completed : result.Outcome == LocalFileAnswerOutcome.Cancelled
                ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        return result;
    }

    public async Task<LocalFileAnswerResult> AnswerFolderAsync(FolderAnswerReview review, Func<bool> nativeCurrent,
        CancellationToken token)
    {
        var origin = HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;
        var callRevision = CallPolicyRevision;
        bool Current() => nativeCurrent() && IsClipboardEligible(origin, callRevision);
        if (!FolderAnswerEligible(review.Source.Reference, origin, Current)
            || !ReferenceEquals(FolderRevision, review.Source))
        {
            return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied);
        }
        using var activity = HostActivity.BeginRoot(review.Target.SourceRequest, HostActivityLayer.Application, HostOperation.Request,
            [new ActivityLink(review.Source.Review.Cause)]);
        var result = await folderAnswer!.AnswerAsync(review, Current, token);
        if (!Current() || !ReferenceEquals(FolderRevision, review.Source))
        {
            result = LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale);
        }
        activity.Complete(result.Outcome is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.Unsupported
            ? HostOperationOutcome.Completed : result.Outcome == LocalFileAnswerOutcome.Cancelled
                ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        return result;
    }

    private bool FolderAnswerEligible(LocalFolderReference exactSource, RequestOrigin origin, Func<bool> current) =>
        !(HostActivity.HasScope && HostActivity.Current is null) && origin == RequestOrigin.LocalUi
        && folderAnswer is not null && current() && FolderRevision is { } source && source.Reference == exactSource
        && (HostActivity.Current is not { } input || input.Request.InvocationId is null
            && input.Request.SessionId == source.Review.Request.SessionId && input.Request.TaskId == source.Review.Request.TaskId);
}
