using System.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Files;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private LocalFileAnswer? fileAnswer;
    public event EventHandler? FileAnswerRequested;

    public void BindFileAnswer(LocalFileAnswer action)
    {
        if (fileAnswer is not null) { throw new InvalidOperationException("File answer action is already bound."); }
        fileAnswer = action;
    }

    public Task RequestFileAnswerAsync() => HostRequestRunner.RunAsync(
        HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi,
        () => RouteTranscriptAsync("answer file", 1, Kora.Core.Auditing.SecurityAuditInitiator.LocalUser,
            Interlocked.Read(ref manualCallSpeechRevision)));

    public async Task<LocalFileAnswerResult> AnswerFileAsync(LocalFileReference exactSource, string question,
        Func<bool> nativeRequestIsCurrent, CancellationToken cancellationToken)
    {
        var origin = HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;
        var callRevision = CallPolicyRevision;
        bool Eligible() => nativeRequestIsCurrent() && IsClipboardEligible(origin, callRevision);
        if (HostActivity.HasScope && HostActivity.Current is null
            || origin != RequestOrigin.LocalUi || fileAnswer is null || !Eligible())
        {
            return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied);
        }
        if (FileRevision is not { } admitted || admitted.Reference != exactSource)
        {
            return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale);
        }
        if (HostActivity.Current is { } incoming
            && (incoming.Request.SessionId != admitted.Review.Request.SessionId
                || incoming.Request.TaskId != admitted.Review.Request.TaskId || incoming.Request.InvocationId is not null))
        {
            return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied);
        }
        // This new native question has its own LocalUi provenance, linked to the original source admission.
        var request = new HostRequest(new(Guid.NewGuid()), admitted.Review.Request.SessionId, admitted.Review.Request.TaskId, origin);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request,
            [new ActivityLink(admitted.Review.Cause)]);
        var result = await fileAnswer.ExecuteAsync(exactSource, question, Eligible, cancellationToken);
        if (!Eligible() || FileRevision?.Reference != exactSource)
        {
            result = LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale);
        }
        activity.Complete(result.Outcome is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.Unsupported or LocalFileAnswerOutcome.NoEvidence
            ? HostOperationOutcome.Completed : result.Outcome == LocalFileAnswerOutcome.Cancelled
                ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        return result;
    }
}
