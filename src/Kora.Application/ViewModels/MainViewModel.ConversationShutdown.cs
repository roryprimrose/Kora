using Kora.Application.Diagnostics;
using Kora.Core.Platform;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<Task<bool>>? conversationShutdown;
    private Func<bool>? conversationShutdownCurrent;

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
