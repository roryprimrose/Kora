using Kora.Core.Voice;

namespace Kora.Core.Communication;

public static class InCallFeedbackRules
{
    public const InCallFeedbackMode Default = InCallFeedbackMode.UI;

    public static InCallFeedbackMode Parse(string value) => value switch
    {
        "Voice" => InCallFeedbackMode.Voice,
        "UI" => InCallFeedbackMode.UI,
        "Both" => InCallFeedbackMode.Both,
        "Inherit" => InCallFeedbackMode.Inherit,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Use exactly Voice, UI, Both or Inherit."),
    };

    public static ResponseOutputMode Resolve(ResponseOutputMode ordinary, InCallFeedbackMode mode, CallState state)
    {
        if (!Enum.IsDefined(ordinary)) { throw new ArgumentOutOfRangeException(nameof(ordinary)); }
        if (!Enum.IsDefined(mode)) { throw new ArgumentOutOfRangeException(nameof(mode)); }
        if (state is not (CallState.Active or CallState.Suspected)) { return ordinary; }
        return mode switch
        {
            InCallFeedbackMode.Voice => ResponseOutputMode.VoiceOnly,
            InCallFeedbackMode.UI => ResponseOutputMode.VisualOnly,
            InCallFeedbackMode.Both => ResponseOutputMode.Hybrid,
            _ => ordinary,
        };
    }
}
