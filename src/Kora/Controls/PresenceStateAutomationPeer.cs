using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Application.Visuals;
using Kora.Core;

namespace Kora.Controls;

internal sealed class PresenceStateAutomationPeer(
    Control owner,
    Func<(AssistantState State, bool IsSpeaking)?> capture) : ControlAutomationPeer(owner)
{
    internal const string UnavailableName = "Kora presence: unavailable";
    private string previousName = GetName(capture());

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
    protected override string GetNameCore() => GetName(capture());
    protected override bool IsControlElementCore() => IsAvailable && base.IsControlElementCore();
    protected override bool IsContentElementCore() => IsAvailable && base.IsContentElementCore();
    protected override AutomationLiveSetting GetLiveSettingCore() =>
        IsAvailable ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off;

    private bool IsAvailable => !string.Equals(GetNameCore(), UnavailableName, StringComparison.Ordinal);

    internal void Refresh()
    {
        Dispatcher.UIThread.VerifyAccess();
        var name = GetNameCore();
        var oldName = previousName;
        previousName = name;
        // Retirement is silent; reads and subsequent events always resolve current eligibility.
        if (!string.Equals(name, UnavailableName, StringComparison.Ordinal)
            && !string.Equals(oldName, name, StringComparison.Ordinal))
        {
            RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, oldName, name);
        }
    }

    internal static (AssistantState State, bool IsSpeaking)? FromRenderInputs(PresenceRenderInputs? inputs) =>
        inputs is { IsPresenceVisible: true } ? (inputs.State, inputs.IsSpeaking) : null;

    private static string GetName((AssistantState State, bool IsSpeaking)? state)
    {
        if (state is not { } current)
        {
            return UnavailableName;
        }
        var description = current.State switch
        {
            AssistantState.Listening => "listening",
            AssistantState.Calculating => "calculating",
            AssistantState.Waiting => "waiting",
            AssistantState.Executing => "executing",
            AssistantState.Success => "success",
            AssistantState.Failure => "failure",
            AssistantState.Information => "information",
            _ => null,
        };
        return description is null ? UnavailableName
            : $"Kora presence: {description}; speech playback {(current.IsSpeaking ? "active" : "inactive")}";
    }
}
