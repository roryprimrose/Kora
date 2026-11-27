using Kora.Core.Auditing;
using Kora.Core.Commands;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private SessionCommand? ParseAddressedSessionCommand(string text, SecurityAuditInitiator initiator) =>
        SessionCommand.Parse(text, AssistantName)
        ?? (initiator == SecurityAuditInitiator.VoiceCommand && IsWakeCommand(text)
            ? SessionCommand.ParseWakeSpokenNameRoute(text) : null);
}
