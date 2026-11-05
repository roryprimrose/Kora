namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private const string SubmittedActivityUnavailable =
        "Submitted activity unavailable. No idle, completion or resource release is inferred. Restore private host access and refresh.";

    public string SubmittedWorkActivityStatus
    {
        get
        {
            if (disposed || !CanRevealPrivatePresentation || remainingWorkConversation is not { } conversation)
            { return SubmittedActivityUnavailable; }
            var privacy = privacyObservation.Current.TopologyRevision;
            var binding = Interlocked.Read(ref remainingWorkBinding);
            var activity = conversation.ReadSubmittedWorkActivity();
            return !disposed && CanRevealPrivatePresentation && privacyObservation.Current.TopologyRevision == privacy
                && Interlocked.Read(ref remainingWorkBinding) == binding
                ? activity.Format() : SubmittedActivityUnavailable;
        }
    }

    public void RefreshSubmittedWorkActivity() => OnPropertyChanged(nameof(SubmittedWorkActivityStatus));

    public void ShowSubmittedWorkActivity()
    {
        ShowApplication();
        RefreshSubmittedWorkActivity();
    }
}
