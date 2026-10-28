using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Platform;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<Task<bool>>? conversationShutdown;
    private Func<bool>? conversationShutdownCurrent;
    private Func<ApplicationLifecycleOperation, RequestOrigin, Func<bool>, Task<ApplicationLifecycleShutdown?>>? submittedWorkShutdown;
    private bool voluntaryShutdownRunning;

    public void BindSubmittedWorkShutdown(
        Func<ApplicationLifecycleOperation, RequestOrigin, Func<bool>, Task<ApplicationLifecycleShutdown?>>? prepare) =>
        submittedWorkShutdown = prepare;

    private async Task<ApplicationLifecycleShutdown?> PrepareSubmittedWorkShutdown(ApplicationLifecycleOperation operation, Func<bool> current)
    {
        var prepare = submittedWorkShutdown;
        if (prepare is null) { return new(current, () => Task.CompletedTask, () => { }); }
        var origin = HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;
        try
        {
            var attempt = await prepare(operation, origin, () => ReferenceEquals(submittedWorkShutdown, prepare) && current());
            if (attempt is null)
            { ShowInformation("Exit or restart cancelled.", "Submitted requests were not stopped. The review's dispatch hold was released."); }
            return attempt;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportApplicationShutdownFailure(exception);
            return null;
        }
    }

    private async Task<bool> JoinSubmittedWorkShutdown(ApplicationLifecycleShutdown attempt)
    {
        try
        {
            await attempt.StopAndJoinAsync();
            if (!attempt.IsCurrent) { throw new InvalidOperationException("Original source authority changed during resource joins."); }
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportApplicationShutdownFailure(exception);
            return false;
        }
    }

    private void ReportApplicationShutdownFailure(Exception exception)
    {
        ApplicationLog.Error(logger, exception, "Joining reviewed submitted work before voluntary shutdown");
        ShowFailure("Exit or restart was not confirmed.",
            "Submitted work, required audit or resource release is unconfirmed. No application effect was performed. Inspect current work before trying again. Failure type: " + exception.GetType().Name);
    }

    public void BindConversationShutdown(Func<Task<bool>>? shutdown, Func<bool>? current = null)
    {
        conversationShutdown = shutdown;
        conversationShutdownCurrent = current;
    }

    private async Task<Func<bool>?> PrepareConversationShutdown()
    {
        try
        {
            await JoinKnowledgeSourcesAsync();
            if (!await claudeShutdown())
            {
                ShowFailure("Exit or restart was not confirmed.", "Claude setup or request resources have not joined. No exit or restart was performed.");
                return null;
            }
            var shutdown = conversationShutdown;
            if (shutdown is null) { return static () => true; }
            var validation = conversationShutdownCurrent;
            var call = CallPolicyRevision;
            var privacy = privacyObservation.Current.TopologyRevision;
            var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
            var owner = settingsOwnershipRevision();
            bool Current() => !disposed && callOwnershipEligible() && !IsPrivacyPresentationHeld
                && sessionController.IsCurrentSessionUnlocked()
                && privacyObservation.Current.SessionState == WindowsSessionState.Unlocked
                && CallPolicyRevision == call && privacyObservation.Current.TopologyRevision == privacy
                && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding && settingsOwnershipRevision() == owner
                && ReferenceEquals(conversationShutdown, shutdown) && ReferenceEquals(conversationShutdownCurrent, validation)
                && (validation?.Invoke() ?? true);
            if (!IsCallMutationHostEligible || !await shutdown())
            {
                ShowInformation("Exit or restart cancelled.", "Unsent input remains open unless privacy retirement cleared it. Nothing was saved or submitted.");
                return null;
            }
            return RequireConversationShutdownCurrent(Current) ? Current : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ApplicationLog.Error(logger, exception, "Reviewing unsent input before voluntary shutdown");
            ShowFailure("Exit or restart was not confirmed.", "Unsent input review failed. Inspect the open conversation before trying again. Failure type: " + exception.GetType().Name);
            return null;
        }
    }

    private bool RequireConversationShutdownCurrent(Func<bool> current)
    {
        try
        {
            if (current()) { return true; }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ApplicationLog.Error(logger, exception, "Revalidating unsent input before voluntary shutdown");
        }
        ShowFailure("Exit or restart was not confirmed.", "Conversation input or private ownership changed after review. Inspect current input and make a new deliberate decision.");
        return false;
    }
}
