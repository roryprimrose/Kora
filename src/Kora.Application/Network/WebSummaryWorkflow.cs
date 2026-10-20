using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Tools.Network;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Network;

/// <summary>Owns exact source-use review, one-use confirmation, and late-result admission.</summary>
public sealed partial class WebSummaryWorkflow(
    WebResultSummarize action, IWebSummaryModelExecution execution, ISecurityAuditLog audit,
    TimeProvider time, ILogger<WebSummaryWorkflow> logger)
{
    private readonly Lock gate = new();
    private WebSummaryReview? pending;
    private long generation;

    public WebSummaryReview Prepare(WebResultSnapshot snapshot, Func<bool> current)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(current);
        RequireOriginalUser();
        Retire();
        var now = time.GetUtcNow();
        var issuer = HostActivity.RequireCurrent();
        action.RequireCredentialScreenAccepted(snapshot);
        var target = LocalContextSessionTarget.ReviewedFresh(issuer.Request, HostRequest.Create(issuer.Request.Origin));
        lock (gate)
        {
            pending = null;
            if (!current()) { throw new InvalidOperationException("The exact web-result source is no longer current."); }
            return pending = new(snapshot, current, now.AddMinutes(2), target, issuer.Activity!.Context);
        }
    }

    public void Retire()
    {
        lock (gate) { pending = null; Interlocked.Increment(ref generation); }
    }

    public async Task<ModelTurnResult> Confirm(Guid exactReview, Func<bool> current,
        SecurityAuditInitiator initiator, CancellationToken token)
    {
        RequireOriginalUser();
        var confirmation = HostActivity.RequireCurrent();
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "network.summarize-web-result", SecurityAuditOutcome.Requested, initiator, "network.volatile-web-result");
        audit.Write(requested);
        ModelTurnResult result;
        try
        {
            WebSummaryReview? review;
            long admittedGeneration;
            lock (gate)
            {
                review = pending?.Id == exactReview ? pending : null;
                pending = null;
                admittedGeneration = Interlocked.Read(ref generation);
            }
            bool Live() => review is not null && !token.IsCancellationRequested && current()
                && confirmation.Request.Origin == review.Target.Request.Origin
                && Interlocked.Read(ref generation) == admittedGeneration
                && time.GetUtcNow() < review.ExpiresAt && review.Current();
            if (token.IsCancellationRequested)
            { result = new(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled); }
            else if (!Live())
            { result = new(ModelTurnOutcome.Denied, ModelTurnReason.ContextMismatch); }
            else
            {
                result = await execution.Run(review!, Live, token).ConfigureAwait(false);
                if (!Live())
                {
                    result = new(token.IsCancellationRequested ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Denied,
                        token.IsCancellationRequested ? ModelTurnReason.CallerCancelled : ModelTurnReason.ContextExpired);
                }
                // Source-derived output may not become a model action, approval, grant or interactive question.
                else if (result.Outcome == ModelTurnOutcome.Succeeded
                    && (result.Response is not { Answer: { } answer, Action: null, GrantChange: null, Question: null }
                        || string.IsNullOrWhiteSpace(answer)))
                { result = new(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
            }
            audit.Write(requested.WithOutcome(result.Outcome == ModelTurnOutcome.Succeeded ? SecurityAuditOutcome.Succeeded
                : result.Outcome == ModelTurnOutcome.Cancelled ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Denied,
                result.Outcome == ModelTurnOutcome.Succeeded ? "summary-presentable" : "summary-not-admitted"));
            activity.Complete(result.Outcome == ModelTurnOutcome.Succeeded ? HostOperationOutcome.Completed
                : result.Outcome == ModelTurnOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            return result;
        }
        catch (OperationCanceledException)
        {
            Cancelled();
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Cancelled, "cancelled"));
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            Failed(exception.GetType().Name);
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Failed, "summary-unconfirmed"));
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private static void RequireOriginalUser()
    {
        var current = HostActivity.RequireCurrent();
        if (current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown
            || current.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("Local web summarization requires live original-user input."); }
    }
}
