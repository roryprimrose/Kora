using System.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Kora.Application.KnowledgeSources;

public sealed partial class KnowledgeSourceAnswerWorkflow(KnowledgeSourceManager manager,
    ILocalFileRetrieval retrieval, ILocalFileModelUsePolicy sourceUse, IModelContextRunner model,
    ISecurityAuditLog audit, TimeProvider time, ILogger<KnowledgeSourceAnswerWorkflow> logger,
    SessionWorkspaceService? sessions = null, SessionConversationService? conversations = null)
{
    private KnowledgeSourceAnswerUse? use;
    private SessionWorkspaceEntry? retainedTarget;
    private Func<CancellationToken, Task>? retainedValidation;

    public bool RetainsConversation => sessions is not null || conversations is not null;

    public async Task<KnowledgeSourceAnswerPreparation> PrepareAsync(KnowledgeSource exact, string question,
        Func<bool> eligible, CancellationToken token) =>
        await PrepareAsync([exact], question, eligible, token).ConfigureAwait(false);

    public async Task<KnowledgeSourceAnswerPreparation> PrepareAsync(IReadOnlyList<KnowledgeSource> sources, string question,
        Func<bool> eligible, CancellationToken token) =>
        await PrepareCoreAsync(sources, question, eligible, token).ConfigureAwait(false);

    public Task<KnowledgeSourceAnswerPreparation> PrepareForSessionAsync(KnowledgeSource source,
        KnowledgeSourceReference revision, SessionWorkspaceEntry target, string question, Func<bool> eligible,
        Func<CancellationToken, Task> validateBinding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(validateBinding);
        if (!target.Authority.IsActive || sessions is null || conversations is null)
        { throw new InvalidOperationException("Bound answering requires the exact active canonical conversation."); }
        return PrepareCoreAsync([source], question, eligible, token, target, revision, validateBinding);
    }

    private async Task<KnowledgeSourceAnswerPreparation> PrepareCoreAsync(IReadOnlyList<KnowledgeSource> sources,
        string question, Func<bool> eligible, CancellationToken token, SessionWorkspaceEntry? target = null,
        KnowledgeSourceReference? expectedRevision = null, Func<CancellationToken, Task>? validateBinding = null)
    {
        var host = HostActivity.RequireCurrent();
        if (host.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || host.Request.InvocationId is not null || host.Outcome != HostOperationOutcome.Unknown || !eligible())
        { return new(LocalFileAnswerOutcome.Denied); }
        KnowledgeSourceAnswerUse? selectedUse = null;
        var result = await Audited("review", async () =>
        {
            if (!LocalFileRetrievalPolicy.TryQuery(question, out _))
            { return new KnowledgeSourceAnswerPreparation(LocalFileAnswerOutcome.InvalidQuestion); }
            selectedUse = await manager.OpenAnswerUseAsync(sources, eligible, token).ConfigureAwait(false);
            return await selectedUse.RunAsync(async cancellation =>
            {
                if (expectedRevision is not null && selectedUse.Sources[0].Revision.Reference != expectedRevision)
                { throw new InvalidDataException("The associated revision changed; no newer revision was selected."); }
                if (validateBinding is not null) { await validateBinding(cancellation).ConfigureAwait(false); }
                var snapshots = selectedUse.Sources.Select(item => item.Revision).ToArray();
                var found = await Task.Run(() => snapshots.Length == 1
                    ? retrieval.Search(snapshots[0], snapshots[0].Reference, question, time.GetUtcNow(), cancellation)
                    : retrieval.Search(snapshots, question, time.GetUtcNow(), cancellation), cancellation).ConfigureAwait(false);
                await selectedUse.ValidateAsync(cancellation).ConfigureAwait(false);
                if (found.Outcome == LocalFileSearchOutcome.NoMatch)
                { return new KnowledgeSourceAnswerPreparation(LocalFileAnswerOutcome.NoEvidence); }
                var destination = target is null
                    ? LocalContextSessionTarget.ReviewedFresh(host.Request, HostRequest.Create(host.Request.Origin))
                    : LocalContextSessionTarget.Existing(host.Request, new(new(Guid.NewGuid()), target.Authority.SessionId,
                        new(Guid.NewGuid()), host.Request.Origin), target.Authority.Generation);
                var review = new KnowledgeSourceAnswerReview(selectedUse.Sources, question, found, destination);
                selectedUse.Review = review;
                return new KnowledgeSourceAnswerPreparation(LocalFileAnswerOutcome.Succeeded, review);
            }, token).ConfigureAwait(false);
        }, value => value.Outcome, () => new(LocalFileAnswerOutcome.Unavailable)).ConfigureAwait(false);
        if (result.Review is null && selectedUse is not null)
        { await manager.ReleaseAnswerUseAsync(selectedUse).ConfigureAwait(false); }
        else if (result.Review is not null)
        {
            use = selectedUse;
            retainedTarget = target;
            retainedValidation = validateBinding;
        }
        return result;
    }

    public async Task<LocalFileAnswerResult> AnswerAsync(KnowledgeSourceAnswerReview review, Func<bool> eligible,
        CancellationToken token)
    {
        var input = HostActivity.RequireCurrent();
        if (input.Request.Origin != RequestOrigin.LocalUi || input.Request.InvocationId is not null
            || input.Outcome != HostOperationOutcome.Unknown || !eligible()
            || use is not { Review: { } prepared } exact || !review.IsFromSameRetrieval(prepared)
            || !review.IsCurrent(time.GetUtcNow()))
        { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied); }
        exact.Review = review;
        var target = retainedTarget;
        var bindingValidation = retainedValidation;
        if (target is not null && input.Request.RequestId == review.Target.SourceRequest.RequestId)
        { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied); }
        async Task Validate(CancellationToken cancellation)
        {
            await exact.ValidateAsync(cancellation).ConfigureAwait(false);
            if (bindingValidation is not null) { await bindingValidation(cancellation).ConfigureAwait(false); }
        }
        if (!exact.IsCurrent || !review.TryConsume())
        { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied); }
        ActivityLink[] causes = [new(input.Activity!.Context), .. review.Sources
            .Select(item => item.Revision.Review.Cause).Where(cause => cause != default).Distinct()
            .Select(cause => new ActivityLink(cause))];
        using var activity = HostActivity.BeginRoot(review.Target.SourceRequest, HostActivityLayer.Application,
            HostOperation.Request, causes);
        var result = await Audited("local", () => exact.RunAsync(async cancellation =>
        {
            await Validate(cancellation).ConfigureAwait(false);
            var remaining = review.ExpiresAt - time.GetUtcNow();
            if (remaining <= TimeSpan.Zero) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
            using var expiry = new CancellationTokenSource(remaining, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, expiry.Token);
            cancellation = linked.Token;
            bool Current() => eligible() && exact.IsCurrent;
            await Validate(cancellation).ConfigureAwait(false);
            if (review.Sources.Count > 1)
            {
                var risk = LocalSourceRiskPolicy.Evaluate(review.Search.Citations.Select(review.DescribeCitation).ToArray(), cancellation);
                if (risk != LocalSourceRiskOutcome.AdmittedPrivateLocalOnly)
                {
                    return LocalFileAnswerResult.Empty(risk == LocalSourceRiskOutcome.DetectedRisk
                        ? LocalFileAnswerOutcome.Denied : LocalFileAnswerOutcome.SourceUseUnavailable);
                }
            }
            foreach (var file in review.Sources.SelectMany(item => item.Revision.Files))
            {
                var citations = review.Search.Citations.Where(item => item.Source == file.Reference).ToArray();
                if (citations.Length == 0) { continue; }
                var selected = review.Search with
                {
                    Citations = Array.AsReadOnly(citations), MatchingChunks = citations.Length, Truncated = false,
                };
                var decision = await sourceUse.AdmitAsync(file, review.Question, selected, cancellation).ConfigureAwait(false);
                await Validate(cancellation).ConfigureAwait(false);
                if (decision != LocalFileModelUseDecision.Allowed)
                {
                    return LocalFileAnswerResult.Empty(decision is LocalFileModelUseDecision.Unavailable
                        or LocalFileModelUseDecision.SourceUsePolicyMissing ? LocalFileAnswerOutcome.SourceUseUnavailable
                            : LocalFileAnswerOutcome.Denied) with { SourceUseDecision = decision };
                }
            }
            if (RetainsConversation)
            {
                return await AnswerRetainedAsync(review, target, Validate, Current, cancellation).ConfigureAwait(false);
            }
            var admission = await model.AdmitAsync(review.Target, Current, cancellation, exact.ValidateAsync).ConfigureAwait(false);
            if (admission.Turn is not { } admitted)
            { return Receipt(review, null, admission.Rejection ?? throw new InvalidOperationException("Missing local admission result.")); }
            await using var turn = admitted;
            await exact.ValidateAsync(cancellation).ConfigureAwait(false);
            var context = new KnowledgeSourceAnswerContext(review, turn.Request, turn.SessionGeneration, time.GetUtcNow());
            if (context.Envelope.Serialize().Length > ModelContextEnvelope.MaximumInputUtf8Bytes)
            { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.ContextLimit); }
            var receipt = await turn.RunAsync(context.Envelope, cancellation).ConfigureAwait(false);
            await exact.ValidateAsync(cancellation).ConfigureAwait(false);
            if (!Current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
            return Receipt(review, context, receipt);
        }, token), value => value.Outcome, () => LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Unavailable)).ConfigureAwait(false);
        if (!eligible() || !exact.IsCurrent)
        { result = LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
        activity.Complete(ActivityOutcome(result.Outcome));
        return result;
    }

    private async Task<LocalFileAnswerResult> AnswerRetainedAsync(KnowledgeSourceAnswerReview review,
        SessionWorkspaceEntry? reviewedTarget, Func<CancellationToken, Task> validate,
        Func<bool> current, CancellationToken token)
    {
        var conversation = conversations
            ?? throw new InvalidOperationException("Canonical retained source answering is not composed.");
        var workspace = sessions
            ?? throw new InvalidOperationException("Canonical retained source session creation is not composed.");
        await validate(token).ConfigureAwait(false);
        var target = reviewedTarget
            ?? await workspace.CreateLocalContextSessionAsync(review.Target.SourceRequest, current, token).ConfigureAwait(false);
        var contexts = new Dictionary<HostRequest, KnowledgeSourceAnswerContext>(ReferenceEqualityComparer.Instance);
        var answer = LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.InvalidResponse);
        ModelContextEnvelope Create(HostRequest request, DateTimeOffset now)
        {
            var binding = LocalContextSessionTarget.Existing(review.Target.SourceRequest, request, target.Authority.Generation);
            var context = new KnowledgeSourceAnswerContext(review, binding, request, target.Authority.Generation, now);
            contexts.Add(request, context);
            return context.Envelope;
        }
        ModelTurnResult Resolve(ModelTurnResult result)
        {
            var projected = contexts[result.Provenance!.Request].ResolveRetained(result);
            answer = projected.Answer;
            return projected.Result;
        }
        ActivityLink[] links = [new(HostActivity.RequireCurrent().Activity!.Context)];
        var retained = await conversation.SubmitReviewedContextAsync(target, review.Question, Create, current,
            validate, links, review.Target.SourceRequest.Origin, token, resolveResponse: Resolve,
            joinCancelledResources: true).ConfigureAwait(false);
        await validate(token).ConfigureAwait(false);
        if (!current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
        var outcome = retained.Message.Outcome;
        if (outcome == ModelTurnOutcome.Succeeded)
        {
            return answer with { ExecutionReason = LocalContextTurnReason.Completed, ExecutionState = retained.Task.State };
        }
        return LocalFileAnswerResult.Empty(outcome switch
        {
            ModelTurnOutcome.Cancelled => LocalFileAnswerOutcome.Cancelled,
            ModelTurnOutcome.Denied or ModelTurnOutcome.DeniedEgress => LocalFileAnswerOutcome.Denied,
            ModelTurnOutcome.Unknown => LocalFileAnswerOutcome.InvalidResponse,
            _ => LocalFileAnswerOutcome.Unavailable,
        }) with { ModelReason = retained.Message.Reason, ExecutionState = retained.Task.State };
    }

    private async Task<T> Audited<T>(string operation, Func<Task<T>> action, Func<T, LocalFileAnswerOutcome> outcome,
        Func<T> unavailable)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        var entry = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "knowledge.answer." + operation, SecurityAuditOutcome.Requested,
            HostActivity.RequireCurrent().Request.Origin == RequestOrigin.ActivatedVoice
                ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser, "knowledge.selected-retained");
        audit.Write(entry);
        T result;
        try { result = await action().ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            audit.Write(entry.WithOutcome(SecurityAuditOutcome.Cancelled));
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException
            or InvalidDataException or System.Security.Cryptography.CryptographicException)
        {
            Failure(exception.GetType().Name);
            result = unavailable();
        }
        catch
        {
            audit.Write(entry.WithOutcome(SecurityAuditOutcome.Failed));
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        var terminal = outcome(result);
        audit.Write(entry.WithOutcome(terminal is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.NoEvidence
            or LocalFileAnswerOutcome.Unsupported ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Failed,
            terminal.ToString().ToLowerInvariant()));
        activity.Complete(ActivityOutcome(terminal));
        return result;
    }

    private static HostOperationOutcome ActivityOutcome(LocalFileAnswerOutcome outcome) =>
        outcome is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.NoEvidence or LocalFileAnswerOutcome.Unsupported
            ? HostOperationOutcome.Completed : HostOperationOutcome.Failed;

    private static LocalFileAnswerResult Receipt(KnowledgeSourceAnswerReview review, KnowledgeSourceAnswerContext? context,
        LocalContextTurnReceipt receipt)
    {
        if (!ReferenceEquals(receipt.Request, review.Target.Request)
            || !Enum.IsDefined(receipt.Outcome) || !Enum.IsDefined(receipt.Reason)
            || receipt.Outcome == ModelTurnOutcome.Succeeded
                && (receipt.DurableState != HostTaskState.Succeeded || receipt.Reason != LocalContextTurnReason.Completed))
        { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.InvalidResponse); }
        var result = context is not null && receipt.ModelResult is { } modelResult && modelResult.Outcome == receipt.Outcome
            ? context.Resolve(modelResult) : LocalFileAnswerResult.Empty(receipt.Outcome switch
            {
                ModelTurnOutcome.Succeeded => LocalFileAnswerOutcome.InvalidResponse,
                ModelTurnOutcome.Cancelled => LocalFileAnswerOutcome.Cancelled,
                ModelTurnOutcome.Denied or ModelTurnOutcome.DeniedEgress => LocalFileAnswerOutcome.Denied,
                _ => LocalFileAnswerOutcome.Unavailable,
            });
        return result with { ExecutionReason = receipt.Reason, ExecutionState = receipt.DurableState };
    }
}
