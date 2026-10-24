using Kora.Application.Mcp;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<McpCommand, RequestOrigin, CancellationToken, Task<string>>? mcpCommands;

    public void BindMcpCommands(Func<McpCommand, RequestOrigin, CancellationToken, Task<string>>? commands) =>
        mcpCommands = commands;

    private async Task ExecuteMcpCommandAsync(McpCommand command, SecurityAuditInitiator initiator)
    {
        if (command.Error is not null) { ShowFailure("Clarify the MCP task.", command.Error); return; }
        if (!IsHostInputEligible || mcpCommands is null)
        {
            ShowFailure("MCP is unavailable.", "An exact foreground session, explicit server/account selection and native reviews are required. Nothing was submitted.");
            return;
        }
        var origin = initiator == SecurityAuditInitiator.VoiceCommand ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
        var status = await mcpCommands(command, origin, CancellationToken.None);
        if (!IsHostInputEligible) { return; }
        ShowInformation(command.Stop ? "MCP cancellation requested." : "MCP operation result.", status);
    }
}
