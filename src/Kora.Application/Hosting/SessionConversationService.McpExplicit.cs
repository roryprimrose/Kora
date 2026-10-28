using Kora.Application.Mcp;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Mcp;
using Kora.Core.Storage;
using Kora.Core.Tools;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    /// <summary>Original-user MCP execution through the canonical task, approval, transport and cancellation owners; no model turn.</summary>
    public async Task<McpExplicitResult> InvokeMcpAsync(SessionWorkspaceEntry target, string serverId,
        McpAccountReference account, McpApprovalPresenter present, McpToolSelector select,
        McpArgumentsPresenter input, Func<bool> admitted, CancellationToken token,
        RequestOrigin origin = RequestOrigin.LocalUi)
    {
        RequireOriginalSource(origin);
        if (mcpConfiguration is null || mcpBroker is null || mcpEffects is null || interactions is null
            || cancellationAudit is null || explicitTaskCoordinator is null || store is not ILocalContextTaskStore tasks)
        { throw new McpUnavailableException(); }
        using var lifecycle = EnterLifecycleSubmission(target, "Explicit MCP request: " + serverId);
        var control = access.ControlRevision;
        bool Live() => access.CanControl && access.ControlRevision == control && admitted();
        token.ThrowIfCancellationRequested();
        if (!Live() || !target.Authority.IsActive
            || await store.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false) != target || !Live())
        { throw new InvalidOperationException("The exact active MCP session is no longer current."); }
        var request = new HostRequest(new(Guid.NewGuid()), target.Authority.SessionId, new(Guid.NewGuid()), origin);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var prior = await store.RecordControlIntentAsync(request, token).ConfigureAwait(false);
        ConversationCancellationLease? running = null;
        McpInvocation? invocation = null;
        McpModelResult? observed = null;
        var outcome = McpExplicitOutcome.Unknown;
        var confirmed = false;
        try
        {
            try
            {
                if (!Live() || await store.ReadMetadataAsync(request.SessionId, token).ConfigureAwait(false) != target || !Live())
                { throw new InvalidOperationException("Original MCP input admission changed."); }
                prior = await tasks.AdmitLocalContextDispatchAsync(request, target.Authority.Generation, Live, token).ConfigureAwait(false);
                var now = time.GetUtcNow();
                running = RegisterRunningConversation(new(Guid.NewGuid(), request, target.Authority.Generation,
                    prior.Revision, now, now.AddMinutes(2)), Live, token);
                running.Dispatched(prior.Revision);
                RunningConversationChanged?.Invoke(running.Status);
                invocation = new(mcpConfiguration, mcpBroker, mcpEffects, interactions, store, access,
                    cancellationAudit, time, request, target.Authority.Generation, serverId, account, present, select,
                    () => running.CanContinue);
                invocation.BindOriginalUserTask(prior);
                await invocation.PrepareAsync(running.Token).ConfigureAwait(false);
                var reply = await invocation.InvokeOriginalUserAsync(input, running.Token).ConfigureAwait(false);
                outcome = McpExplicitResult.From(reply.Outcome);
                observed = reply.Mcp;
                await invocation.ValidateAsync(running.Token).ConfigureAwait(false);
            }
            catch (McpUnavailableException) { outcome = McpExplicitOutcome.Unavailable; }
            catch (OperationCanceledException) { outcome = McpExplicitOutcome.Cancelled; }
            catch (InvalidDataException exception)
            {
                Failure(logger, exception.GetType().Name);
                outcome = McpExplicitOutcome.Denied;
            }
            catch (InvalidOperationException exception)
            {
                Failure(logger, exception.GetType().Name);
                outcome = McpExplicitOutcome.Denied;
            }
            catch (IOException exception)
            {
                Failure(logger, exception.GetType().Name);
                outcome = McpExplicitOutcome.Unknown;
            }
            // A cancellation request never abandons an exchange. The awaited operation above and disposal below are physical joins.
            if (invocation is not null)
            {
                await invocation.JoinTransportAsync().ConfigureAwait(false);
                if (outcome == McpExplicitOutcome.Succeeded)
                {
                    try { await invocation.ValidateAsync(running!.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { outcome = McpExplicitOutcome.Cancelled; }
                    catch (InvalidOperationException) { outcome = McpExplicitOutcome.Denied; }
                }
            }
            if (running?.BeginCompletion() == true || token.IsCancellationRequested)
            { outcome = McpExplicitOutcome.Cancelled; }
            if (!Live() || await store.ReadMetadataAsync(request.SessionId, CancellationToken.None).ConfigureAwait(false) != target || !Live())
            { outcome = McpExplicitOutcome.Denied; }
            if (outcome != McpExplicitOutcome.Succeeded) { observed = null; }
            var state = outcome == McpExplicitOutcome.Succeeded ? HostTaskState.Succeeded
                : prior.State == HostTaskState.DispatchRecorded ? HostTaskState.Unknown
                : outcome == McpExplicitOutcome.Cancelled ? HostTaskState.Cancelled : HostTaskState.Failed;
            var terminal = await explicitTaskCoordinator.RecordOutcomeAsync(prior, state, CancellationToken.None).ConfigureAwait(false);
            var readback = await store.ReadTaskAsync(request.SessionId, request.TaskId, CancellationToken.None).ConfigureAwait(false);
            if (readback?.Task != terminal)
            { throw new IOException("The original MCP terminal receipt is unconfirmed. Do not retry."); }
            if (outcome == McpExplicitOutcome.Succeeded)
            {
                try { await invocation!.ValidateResultSourceAsync(running!.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { observed = null; outcome = McpExplicitOutcome.Cancelled; }
                catch (InvalidOperationException) { observed = null; outcome = McpExplicitOutcome.Denied; }
            }
            confirmed = true;
            running?.Complete(outcome switch
            {
                McpExplicitOutcome.Succeeded => ModelTurnOutcome.Succeeded,
                McpExplicitOutcome.Cancelled => ModelTurnOutcome.Cancelled,
                McpExplicitOutcome.Denied => ModelTurnOutcome.Denied,
                McpExplicitOutcome.Unavailable => ModelTurnOutcome.Unavailable,
                _ => ModelTurnOutcome.Unknown,
            }, terminal.State);
            if (!Live() || token.IsCancellationRequested
                || await store.ReadMetadataAsync(request.SessionId, CancellationToken.None).ConfigureAwait(false) != target || !Live())
            { observed = null; outcome = token.IsCancellationRequested ? McpExplicitOutcome.Cancelled : McpExplicitOutcome.Denied; }
            activity.Complete(outcome == McpExplicitOutcome.Succeeded ? HostOperationOutcome.Completed
                : outcome == McpExplicitOutcome.Cancelled ? HostOperationOutcome.Cancelled
                : outcome == McpExplicitOutcome.Unknown ? HostOperationOutcome.Unknown : HostOperationOutcome.Failed);
            return new(request, outcome, terminal.State, observed);
        }
        finally
        {
            var transportDisposal = invocation?.DisposeAsync().AsTask() ?? Task.CompletedTask;
            var cancellationDisposal = running?.DisposeAsync().AsTask() ?? Task.CompletedTask;
            await Task.WhenAll(transportDisposal, cancellationDisposal).ConfigureAwait(false);
            if (running is not null) { ReleaseRunningConversation(running, confirmed); }
        }
    }
}
