using Kora.Core.Conversation;

namespace Kora.Application.Hosting;

public sealed partial class NewRequestComposer
{
    public ConversationTextMeasurement? DraftMeasurement =>
        !closed && access.CanInspect ? ConversationText.Measure(body) : null;
}
