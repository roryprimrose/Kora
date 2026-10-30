using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Application.Hosting.SessionRouting;
using Kora.Application.ViewModels;
using Kora.Core.Conversation.SessionRouting;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed class SessionRoutingController : IDisposable
{
    private readonly MainViewModel main;
    private readonly SessionWorkspaceService workspace;
    private readonly SessionRoutingService routing;
    private SessionRoutingWindow? window;
    private SessionRoutingAdviceWindow? adviceWindow;
    private bool adviceCancelled;
    private bool busy;
    private bool disposed;
    private Task<SessionRoutingReceipt?>? active;
    private readonly Lock inferenceGate = new();
    private CancellationTokenSource? inference;

    internal SessionRoutingController(MainViewModel main, SessionWorkspaceService workspace,
        SessionConversationService conversation, ISessionWorkspaceAccess access, TimeProvider time,
        ISessionRelatednessMatcher? matcher = null, SessionRoutingControlAdmission? admission = null)
    {
        this.main = main;
        this.workspace = workspace;
        routing = new(workspace, conversation, access, time, matcher, admission, ReviewAdviceAsync);
    }

    internal void Bind()
    {
        main.BindSessionRouting(RouteAsync);
        main.PrivacyClosureRequested += PrivacyClosed;
        workspace.SessionMetadataChanging += SourceChanged;
        workspace.SessionLifecycleChanged += SourceChanged;
        workspace.SessionRetired += SourceChanged;
    }

    internal Task<SessionRoutingReceipt?> RouteAsync(string body, RequestOrigin origin, Func<bool> admitted)
    {
        if (disposed || busy) { throw new InvalidOperationException("An exact destination review already owns routing input, or routing is closed."); }
        busy = true;
        active = RouteCoreAsync(body, origin, admitted);
        return active;
    }

    internal async Task<bool> PrepareShutdown()
    {
        Revoke();
        if (active is { } operation)
        {
            try { await operation; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                main.ReportHostInteractionFailure("Routing stopped before shutdown; inspect any unconfirmed attempt. Failure type: " + exception.GetType().Name);
            }
        }
        return !busy;
    }

    internal Func<bool> CaptureShutdownEligibility()
    {
        var operation = active;
        return () => !busy && ReferenceEquals(active, operation);
    }

    private async Task<SessionRoutingReceipt?> RouteCoreAsync(string body, RequestOrigin origin, Func<bool> admitted)
    {
        using var cancellation = new CancellationTokenSource();
        lock (inferenceGate) { inference = cancellation; }
        adviceCancelled = false;
        try
        {
            var review = await routing.PrepareAsync(body, origin, () => !disposed && admitted(), cancellation.Token);
            if (!routing.IsCurrent(review)) { throw new OperationCanceledException("Original routing admission changed."); }
            var result = new TaskCompletionSource<SessionRoutingSelection?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var opened = new SessionRoutingWindow(routing, review, () => !disposed && admitted(),
                exception => main.ReportHostInteractionFailure("Destination review stopped. No rerouting or submission retry. Failure type: " + exception.GetType().Name));
            window = opened;
            opened.Closed += (_, _) => result.TrySetResult(opened.Result);
            opened.Show();
#pragma warning disable VSTHRD003 // Exact native close completion; awaiting leaves the dispatcher responsive.
            var selection = await result.Task;
#pragma warning restore VSTHRD003
            await opened.ChoiceOperation;
            if (opened.Failure is { } failure) { throw new InvalidOperationException("The exact destination review failed; no submission.", failure); }
            if (selection is null) { return null; }
            return await routing.SubmitAsync(selection, CancellationToken.None);
        }
        catch (OperationCanceledException) when (adviceCancelled) { return null; }
        finally
        {
            routing.Retire();
            lock (inferenceGate) { inference = null; }
            window?.Withdraw();
            window = null;
            adviceWindow?.Withdraw();
            adviceWindow = null;
            busy = false;
        }
    }

    private async Task<bool> ReviewAdviceAsync(SessionRoutingAdvice advice, Func<bool> admitted, CancellationToken token) =>
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            if (disposed || !admitted()) { throw new OperationCanceledException("Local advice disclosure admission changed."); }
            var result = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var opened = new SessionRoutingAdviceWindow(advice, () => !disposed && admitted());
            adviceWindow = opened;
            opened.Closed += (_, _) => result.TrySetResult(opened.Result);
            opened.Show();
            try
            {
                var choice = await result.Task.WaitAsync(token);
                if (choice is { } allow) { return allow; }
                adviceCancelled = true;
                throw new OperationCanceledException("Local advice disclosure cancelled. Nothing submitted.");
            }
            finally
            {
                opened.Withdraw();
                adviceWindow = null;
            }
        });

    private void PrivacyClosed(object? sender, EventArgs args) => Revoke();
    private void SourceChanged(HostId<SessionIdentity> session) => Revoke();

    private void Revoke()
    {
        routing.Retire();
        lock (inferenceGate) { inference?.Cancel(); }
        void Withdraw() { adviceWindow?.Withdraw(); window?.Withdraw(); }
        if (Dispatcher.UIThread.CheckAccess()) { Withdraw(); }
        else { Dispatcher.UIThread.Invoke(Withdraw); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.BindSessionRouting(null);
        main.PrivacyClosureRequested -= PrivacyClosed;
        workspace.SessionMetadataChanging -= SourceChanged;
        workspace.SessionLifecycleChanged -= SourceChanged;
        workspace.SessionRetired -= SourceChanged;
        Revoke();
    }
}
