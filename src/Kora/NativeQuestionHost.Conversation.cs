using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Network;

namespace Kora;

internal sealed partial class NativeQuestionHost
{
    private HostRequest? mcpObservationRequest;
    private long mcpObservationRevision;
    private HostRequest? pageObservationRequest;
    private long pageObservationRevision;

    internal async Task<ConversationQuestionPresentation> AskConversationAsync(HostRequest request,
        HostRevision generation, QuestionSpec spec, DateTimeOffset expiresAt, Func<bool> admitted,
        Func<NativeQuestionViewModel, Task> present, CancellationToken token,
        HostOperationProposal? webProposal = null, Uri? webAddress = null,
        Kora.Application.Mcp.McpReview? mcpReview = null, bool mcpSelection = false)
    {
        if (disposed || !canInteract() || !admitted() || HostActivity.RequireCurrent().Request != request
            || request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || Interlocked.CompareExchange(ref waiting, 1, 0) != 0)
        { throw new InvalidOperationException("A single exact private question presenter is required. No competing question was opened."); }
        var ownership = admissionRevision?.Invoke();
        var privacy = privateRevision?.Invoke();
        bool Eligible() => !disposed && canInteract() && admitted() && !token.IsCancellationRequested
            && time.GetUtcNow() < expiresAt && ownership == admissionRevision?.Invoke() && privacy == privateRevision?.Invoke();
        currentAdmission = Eligible;
        NativeQuestionViewModel? opened = null;
        try
        {
            var task = await store.ReadTaskAsync(request.SessionId, request.TaskId, token);
            var preparingMcp = mcpSelection || mcpReview?.Stage is Kora.Application.Mcp.McpReviewStage.Connection
                or Kora.Application.Mcp.McpReviewStage.ToolSelection;
            if (!Eligible() || task is not { CurrentSource: true }
                || (preparingMcp
                    ? task.Task.State != HostTaskState.IntentRecorded || task.Task.Revision.Value != 1
                    : task.Task.State != HostTaskState.DispatchRecorded || task.Task.Revision.Value != 2)
                || !request.IsWithinIntent(task.Task.Request) || task.Generation != generation)
            { throw new InvalidOperationException("The original conversation task is no longer dispatched in this exact generation."); }
            if (mcpSelection && (webProposal is not null || mcpReview is not null
                || !string.Equals(spec.SourceId, "mcp-tool-selection", StringComparison.Ordinal)))
            { throw new InvalidOperationException("Only an original host-owned MCP selection question may use preparation authority."); }
            if (webProposal is not null && (webProposal.Request != request
                || (mcpReview is null ? webAddress is null || !WebPageAccessBinding.Matches(webProposal, webAddress)
                    : webAddress is not null || !mcpReview.IsBoundTo(webProposal))
                || webProposal.ExpiresAt != expiresAt
                || callObservation is null))
            { throw new InvalidOperationException("The exact page proposal and current native policy observation are required."); }
            var isMcp = mcpReview is not null || mcpSelection;
            var isPage = webProposal is not null && !isMcp;
            if (isMcp && mcpObservationRequest != request)
            {
                if (mcpReview?.Stage != Kora.Application.Mcp.McpReviewStage.Connection)
                { throw new InvalidOperationException("MCP selection and invocation require this native host's original connection review."); }
                mcpObservationRequest = request;
                mcpObservationRevision = 0;
            }
            if (isPage && pageObservationRequest != request)
            {
                pageObservationRequest = request;
                pageObservationRevision = 0;
            }
            var policy = callObservation?.Invoke().Authorization(Eligible(), mandatoryGatesSatisfied: webProposal is not null || mcpSelection)
                ?? new(Eligible(), false, false, true);
            var observation = await store.PublishTrustedSnapshotAsync(request, policy, webProposal,
                expectedObservationRevision: isMcp ? mcpObservationRevision : isPage ? pageObservationRevision : 0, token);
            if (isMcp) { mcpObservationRevision = observation.Value; }
            if (isPage) { pageObservationRevision = observation.Value; }
            var created = webProposal is null
                ? await questions.CreateAsync(request, spec, expiresAt, token)
                : mcpReview is not null
                    ? await authorization.PresentMcpOnceAsync(request, mcpReview, token)
                    : await authorization.PresentWebPageOnceAsync(request, webAddress!, token);
            if (created.Outcome != HostInteractionOutcome.Presented || created.Question is not { } initial
                || initial.SessionGeneration != generation || !Eligible())
            { throw new InvalidOperationException("The exact clarification could not be durably presented."); }
            opened = new(initial, questions, authorization, reviews, Eligible, time, logger);
            if (mcpReview is not null) { opened.BindMcpReview(mcpReview); }
            current = opened;
            Interlocked.Exchange(ref currentFocusRevision, Interlocked.Read(ref focusRevision));
            opened.BindVoiceReplies(voiceReplies, () => ownership is not null && privacy is not null
                && ReferenceEquals(current, opened) && Eligible());
            await present(opened);
            var completed = await opened.Completion.WaitAsync(token);
            return new(initial, completed);
        }
        finally
        {
            opened?.Close("Original clarification presentation ended. Inspect the retained task outcome; no replay or automatic retry was admitted.");
#pragma warning disable VSTHRD003 // Joins this native owner's existing reply retirement without blocking the dispatcher.
            if (opened is not null) { await opened.OperationQuiescence; }
            if (voiceReplies is not null) { await voiceReplies.Quiescence; }
#pragma warning restore VSTHRD003
            current = null;
            currentAdmission = null;
            Volatile.Write(ref waiting, 0);
        }
    }
}
