using Avalonia.Threading;

using System.Globalization;

using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class AllWorkViewModel
{
    private Func<bool>? waitingQuestionAdmission;
    private Task<SessionWorkOverviewPage>? waitingQuestionRead;

    internal Task WaitingQuestionQuiescence => waitingQuestionRead ?? Task.CompletedTask;

    internal void RetireWaitingQuestionsIfStale()
    {
        if (waitingQuestionAdmission is null || waitingQuestionAdmission()) { return; }
        Invalidate();
        if (!closed) { status = "Waiting-question observation retired. No current page or Find authority; Refresh."; }
        Notify();
    }

    internal async Task<WorkspaceSurfaceNavigationOutcome> ReadWaitingQuestionsAsync(WorkspaceSurfaceNavigation navigation)
    {
        if (closed || busy || !access.CanInspect || navigation.Request is not { } request)
        {
            return WorkspaceSurfaceNavigationOutcome.Unavailable;
        }
        if (!navigation.IsAdmitted() || navigation.CancellationToken.IsCancellationRequested)
        {
            return WorkspaceSurfaceNavigationOutcome.Cancelled;
        }
        var current = HostActivity.RequireCurrent();
        if (current.Request != request || request.Origin != navigation.Origin)
        {
            throw new InvalidOperationException("Waiting-question reads require the original host request and channel.");
        }
        Invalidate();
        filter = SessionListFilter.All;
        taskState = null;
        queryText = string.Empty;
        queryKind = SessionListSearchKind.NameSubstring;
        needsAnswer = true;
        order = SessionWorkOverviewPageOrder.Source;
        var revision = access.ControlRevision;
        var admittedEpoch = epoch;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, navigation.CancellationToken);
        bool Current() => !closed && access.CanInspect && revision == access.ControlRevision
            && admittedEpoch == epoch && navigation.IsAdmitted() && !cancellation.IsCancellationRequested;
        waitingQuestionAdmission = Current;
        operation = cancellation;
        busy = true;
        using var registration = cancellation.Token.Register(() =>
        {
            void Retire()
            {
                if (admittedEpoch != epoch) { return; }
                ClearPage();
                Notify();
            }
            if (Dispatcher.UIThread.CheckAccess()) { Retire(); }
            else { Dispatcher.UIThread.Post(Retire); }
        });
        using var activity = HostActivity.BeginChild(HostActivityLayer.Desktop, HostOperation.Request);
        status = "Reading a fresh Needs answer-only bounded observation; no saved name, task or lifecycle filters.";
        Notify();
        try
        {
            // This original task owns every store read, even when a store ignores cancellation.
            waitingQuestionRead = service.ReadWorkOverviewAsync(SessionListFilter.All, true, null, cancellation.Token);
            var result = await waitingQuestionRead;
            if (!Current()) { throw new OperationCanceledException("Original waiting-question admission changed."); }
            PublishPage(result, revision);
            status = "Needs answer only. "
                + result.Records.Sum(row => row.Work?.OmittedQuestions ?? 0).ToString(CultureInfo.InvariantCulture)
                + " pending-question records omitted in observed groups. " + status;
            activity.Complete(HostOperationOutcome.Completed);
            return WorkspaceSurfaceNavigationOutcome.Completed;
        }
        catch (OperationCanceledException)
        {
            ClearPage();
            if (!closed) { status = "Waiting questions cancelled. No late or partial observation is current; Refresh."; }
            activity.Complete(HostOperationOutcome.Cancelled);
            return WorkspaceSurfaceNavigationOutcome.Cancelled;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
            or TimeoutException or UnauthorizedAccessException)
        {
            ClearPage();
            if (!closed) { status = "Waiting questions unavailable: " + exception.GetType().Name + ". No empty success or all-clear; Refresh."; }
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Failed);
            return WorkspaceSurfaceNavigationOutcome.Unavailable;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally
        {
            operation = null;
            busy = false;
            Notify();
            if (closed && !closing) { lifetime.Dispose(); }
        }
    }
}
