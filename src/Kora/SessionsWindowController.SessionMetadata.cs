using Avalonia.Threading;

using Kora.Application.Tools;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    private SessionMetadataAnswerWindow? metadataWindow;
    private CancellationTokenSource? metadataCancellation;
    private Task metadataOperation = Task.CompletedTask;
    private TaskCompletionSource<SessionMetadataReview.Decision>? metadataDecision;
    private long metadataInputRevision;
    private string? metadataSubject;
    private string? metadataCaller;

    private void BindMetadataAnswer()
    {
        main.SessionMetadataRequested += OpenMetadataCommandAsync;
        service.SessionMetadataChanging += RetireMetadataSubject;
        service.SessionLifecycleChanged += RetireMetadataSubject;
        service.SessionRetired += RetireMetadataSubject;
        service.WorkOverviewInvalidated += RetireMetadataWorkSubject;
    }

    private async Task OpenMetadataCommandAsync(SessionMetadataCommand command, RequestOrigin origin, Func<bool> admitted)
    {
        await OpenMetadataAsync(null, admitted, command, origin);
    }

    private async Task OpenMetadataAsync(SessionWorkspaceEntry? caller, Func<bool> admitted,
        SessionMetadataCommand? command = null, RequestOrigin origin = RequestOrigin.LocalUi)
    {
        await JoinMetadataAsync();
        if (disposed || !admitted() || !access.CanInspect || !main.CanRevealPrivatePresentation)
        { main.ReportHostInteractionFailure("The original private metadata input expired; nothing was inspected."); return; }
        var opened = new SessionMetadataAnswerWindow();
        metadataWindow = opened;
        metadataCancellation = new();
        var control = access.ControlRevision;
        var call = main.CallPolicyRevision;
        bool Current() => !disposed && ReferenceEquals(metadataWindow, opened) && admitted()
            && access.CanInspect && access.CanControl && access.ControlRevision == control
            && main.CanRevealPrivatePresentation && main.CallPolicyRevision == call;
        opened.Fields.Caller.Text = command?.Caller ?? caller!.Authority.SessionId.Value.ToString("D");
        metadataCaller = opened.Fields.Caller.Text;
        opened.Fields.Caller.IsReadOnly = true;
        opened.Fields.Subject.Text = command?.Subject ?? string.Empty;
        opened.Fields.Question.Text = command?.Question ?? string.Empty;
        foreach (var field in new[] { opened.Fields.Subject, opened.Fields.Question })
        {
            field.PropertyChanged += (_, args) =>
            {
                if (args.Property != Avalonia.Controls.TextBox.TextProperty) { return; }
                metadataInputRevision++;
                opened.Fields.Clear();
                metadataDecision?.TrySetResult(SessionMetadataReview.Decision.Cancel);
                metadataCancellation?.Cancel();
            };
        }
        opened.Fields.Review.Click += async (_, _) =>
        {
            if (!metadataOperation.IsCompleted) { return; }
            metadataCancellation?.Dispose();
            metadataCancellation = new();
            metadataOperation = RunMetadataAsync(opened, caller, origin, Current);
            await JoinMetadataOperationAsync();
        };
        opened.Fields.Confirm.Click += (_, _) => metadataDecision?.TrySetResult(SessionMetadataReview.Decision.Confirm);
        opened.Fields.Cancel.Click += (_, _) => RetireMetadataAnswer();
        opened.Closed += (_, _) => RetireMetadataAnswer();
        opened.Show();
        if (!Current()) { RetireMetadataAnswer(); }
    }

    private async Task RunMetadataAsync(SessionMetadataAnswerWindow opened, SessionWorkspaceEntry? caller,
        RequestOrigin origin, Func<bool> admitted)
    {
        var revision = metadataInputRevision;
        var token = metadataCancellation!.Token;
        var subject = opened.Fields.Subject.Text;
        var question = opened.Fields.Question.Text ?? string.Empty;
        bool Current() => admitted() && metadataInputRevision == revision && !token.IsCancellationRequested;
        try
        {
            if (!SessionMetadataSnapshot.IsCanonicalId(subject))
            { throw new InvalidOperationException("Enter one canonical exact session ID."); }
            metadataSubject = subject;
            if (caller is null)
            {
                var page = await service.SearchListAsync(SessionListSearchKind.ExactId, SessionListFilter.Active,
                    opened.Fields.Caller.Text!, null, 1, token);
                if (page.Records.Length != 1) { throw new InvalidOperationException("The exact Active caller is unavailable."); }
                caller = page.Records[0];
            }
            if (!Current()) { throw new OperationCanceledException(token); }
            async Task<SessionMetadataReview.Decision> Present(SessionMetadataReview review, CancellationToken cancellation)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!Current()) { throw new OperationCanceledException(cancellation); }
                    opened.Fields.Preview.Text = SessionMetadataReview.Disclosure + "\n\nOriginal question:\n"
                        + review.OriginalQuestion + "\n\nComplete frozen result:\n" + review.Preview;
                    metadataDecision = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    opened.Fields.Confirm.IsEnabled = true;
                });
                return await metadataDecision!.Task.WaitAsync(cancellation);
            }
            var receipt = await (conversation ?? throw new InvalidOperationException("The canonical conversation host is unavailable."))
                .SubmitSessionMetadataAsync(caller, new(Guid.Parse(subject!)), question, origin, Current, Present, token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                opened.Fields.Clear();
                if (!Current()) { return; }
                opened.Fields.Status.Text = "Caller terminal: " + receipt.Message.Outcome + " / " + receipt.Message.Reason
                    + ". Retained in " + caller.Authority.SessionId.Value.ToString("D") + ". "
                    + SessionMetadataSnapshot.Scope;
                opened.Fields.Preview.Text = receipt.Message.AssistantResponse?.Body ?? string.Empty;
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            opened.Fields.Clear();
            main.ReportHostInteractionFailure("Metadata answer cancelled; the submitted operation has joined. Inspect caller receipts before retrying.");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or TimeoutException)
        {
            opened.Fields.Clear();
            main.ReportHostInteractionFailure("Metadata answer unavailable or unconfirmed: " + exception.GetType().Name + ". Inspect the original caller; no automatic retry.");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                metadataDecision = null;
                opened.Fields.Confirm.IsEnabled = false;
            });
        }
    }

    private void RetireMetadataSubject(HostId<SessionIdentity> subject)
    {
        if (string.Equals(metadataSubject, subject.Value.ToString("D"), StringComparison.Ordinal)
            || string.Equals(metadataCaller, subject.Value.ToString("D"), StringComparison.Ordinal))
        { Dispatcher.UIThread.Invoke(RetireMetadataAnswer); }
    }

    private void RetireMetadataWorkSubject(HostId<SessionIdentity> subject)
    {
        if (string.Equals(metadataSubject, subject.Value.ToString("D"), StringComparison.Ordinal))
        { Dispatcher.UIThread.Invoke(RetireMetadataAnswer); }
    }

    private void RetireMetadataAnswer()
    {
        var opened = metadataWindow;
        metadataWindow = null;
        metadataCancellation?.Cancel();
        metadataDecision?.TrySetResult(SessionMetadataReview.Decision.Cancel);
        opened?.Fields.Clear();
        if (opened?.IsVisible == true) { opened.Close(); }
    }

    private async Task JoinMetadataOperationAsync()
    {
#pragma warning disable VSTHRD003 // Join this controller's exact metadata operation, including cancellation-ignoring dependencies.
        await metadataOperation;
#pragma warning restore VSTHRD003
    }

    private async Task JoinMetadataAsync()
    {
        RetireMetadataAnswer();
        await JoinMetadataOperationAsync();
        metadataCancellation?.Dispose();
        metadataCancellation = null;
        metadataSubject = null;
    }

    private void UnbindMetadataAnswer()
    {
        RetireMetadataAnswer();
        main.SessionMetadataRequested -= OpenMetadataCommandAsync;
        service.SessionMetadataChanging -= RetireMetadataSubject;
        service.SessionLifecycleChanged -= RetireMetadataSubject;
        service.SessionRetired -= RetireMetadataSubject;
        service.WorkOverviewInvalidated -= RetireMetadataWorkSubject;
    }
}
