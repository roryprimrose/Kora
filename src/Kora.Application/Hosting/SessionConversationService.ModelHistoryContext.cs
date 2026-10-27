using Kora.Application.Hosting.ModelHistoryContext;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    public async Task<ModelHistoryContextSource> BindModelHistoryContextAsync(SessionWorkspaceEntry target,
        ConversationMessage source, Func<bool> admitted, CancellationToken token)
    {
        RequireOriginalSource(HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), source.Request.Origin), HostActivityLayer.Application, HostOperation.Request);
        try
        {
            RequireOriginalSource(source.Request.Origin);
            source.Validate();
            if (source.Request.InvocationId is not null || source.Request.SessionId != target.Authority.SessionId || source.Generation != target.Authority.Generation
                || source.Outcome != ModelTurnOutcome.Succeeded || source.AssistantResponse is not { } answer
                || !ModelHistoryContextProposal.IsAdvisory(answer.Body))
            { throw new InvalidDataException("Choose a genuine retained HISTORYREVIEW advisory from this exact conversation."); }
            var control = access.ControlRevision;
            bool Live() => !token.IsCancellationRequested && access.CanInspect && access.CanControl
                && access.ControlRevision == control && admitted();
            await workspace.ValidateModelHistoryContextSourceAsync(target, source, Live, token).ConfigureAwait(false);
            if (!Live()) { throw new OperationCanceledException("The original advisory presentation changed."); }
            var binding = new ModelHistoryContextSource(this, target, source, Live, time);
            activity.Complete(HostOperationOutcome.Completed);
            return binding;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    public Task<ConversationHistoryReview> ReviewModelHistoryContextAsync(ModelHistoryContextSource source,
        IEnumerable<ConversationHistorySelection> selections, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.RequireOwner(this, reviewing: true);
        return ReviewHistoryCoreAsync(source.Target, source.Message.Body, selections, admitted, source.Origin, source, token);
    }
}
