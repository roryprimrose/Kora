using Kora.Core.Tools;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private string claudeObservationIds = ReadOnlyCapabilityCatalog.Version + "," + ReadOnlyCapabilityCatalog.Readiness
        + "," + ReadOnlyCapabilityCatalog.RuntimeList + "," + ReadOnlyCapabilityCatalog.RuntimeStatus;
    private long claudeObservationRevision;
    public string ClaudeObservationIds
    {
        get => claudeObservationIds;
        set { claudeObservationIds = value; Interlocked.Increment(ref claudeObservationRevision); OnPropertyChanged(); }
    }
    public Task SubmitClaudeConversationAsync() => SubmitConversationCoreAsync(allowQuestion: false, claude: true);
    public Task SubmitClaudeAnswerStreamAsync() => SubmitConversationCoreAsync(allowQuestion: false, claude: true, claudeStreamAnswer: true);
    public Task SubmitClaudeObservationsAsync() => SubmitConversationCoreAsync(allowQuestion: false, claude: true, claudeObservations: true);
}
