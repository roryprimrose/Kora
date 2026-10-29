using System.Globalization;

using Kora.Core.Conversation;

namespace Kora;

internal sealed record ObservationDetailChoice(ConversationMessage Message, RetainedLocalObservation Observation, int Position)
{
    public string Name => Observation.CapabilityId + " message " + Message.Sequence.ToString(CultureInfo.InvariantCulture)
        + " result " + Position.ToString(CultureInfo.InvariantCulture);
    public string Label => "Open observed result: " + Name;
}
