using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed class SessionAnswerReadWorkflow(SessionWorkspaceService workspace, ISessionAnswerPlayback playback)
{
    public async Task<string> ReadOnceAsync(SessionWorkspaceEntry target, ConversationMessage expected,
        Func<bool> admitted, CancellationToken token)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Presentation);
        var request = HostActivity.RequireCurrent().Request;
        try
        {
            async Task Validate(CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                expected.Validate();
                if (request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
                    || request.InvocationId is not null || HostActivity.Current?.Request != request
                    || request.SessionId != target.Authority.SessionId
                    || expected.Request.SessionId != request.SessionId
                    || expected.AssistantResponse is null || !admitted())
                { throw new InvalidOperationException("Select one exact retained ASSISTANT answer in this conversation; no current-answer fallback is permitted."); }
                var answer = await workspace.ReadRetainedAssistantAnswerAsync(target, expected, admitted, cancellation);
                if (answer != expected.AssistantResponse || !admitted())
                { throw new InvalidOperationException("The exact retained answer source changed; nothing was substituted."); }
            }
            await Validate(token);
            var result = await playback.ReadAnswerOnceAsync(expected, admitted, Validate, token);
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
