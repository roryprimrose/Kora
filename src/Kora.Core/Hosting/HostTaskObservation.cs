using Kora.Core.Interaction;
using Kora.Core.Interaction.ConversationNotices;
using System.Text.Json.Serialization;

namespace Kora.Core.Hosting;

public sealed record HostTaskObservation(
    HostTaskRecord Task, HostRevision Generation, string Source,
    bool CurrentSource, HostQuestionRecord? Question)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ConversationNoticeAuthority? ConversationNotice { get; init; }
}
