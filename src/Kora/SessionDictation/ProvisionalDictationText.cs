using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace Kora;

/// <summary>Nonselectable private hypotheses; the automation peer exposes a stable label, never changing private text.</summary>
internal sealed class ProvisionalDictationText : TextBlock
{
    protected override AutomationPeer OnCreateAutomationPeer() => new PrivatePeer(this);

    private sealed class PrivatePeer(ProvisionalDictationText owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override string GetNameCore() => "Provisional transcription, may change. Review the accessible final transcript after release.";
    }
}
