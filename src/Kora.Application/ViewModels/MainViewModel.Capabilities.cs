using System.Text;
using System.Text.Json;
using Kora.Application.Tools;
using Kora.Core.Tools;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly ReadOnlyCapabilityRegistry capabilityRegistry;

    internal bool TryPresentCapabilityCommand(string normalizedTranscript)
    {
        var command = ReadOnlyCapabilityCatalog.MatchCommand(normalizedTranscript);
        if (command is null)
        {
            return false;
        }
        if (!IsHostInputEligible)
        {
            throw new InvalidOperationException("Read-only discovery requires an eligible current host.");
        }
        var input = command.InvalidInput ? "null"
            : command.TargetId is null ? "{}"
            : JsonSerializer.Serialize(new CapabilityIdInput(command.TargetId), capabilityCommandJson);
        var reply = capabilityRegistry.Invoke(capabilityRegistry.Admit(CapabilityLane.Native),
            command.Id, input, CancellationToken.None);
        ShowInformation(reply.Outcome == CapabilityOutcome.Succeeded
                ? "Read-only host observation" : "Read-only request denied or unavailable",
            Encoding.UTF8.GetString(ReadOnlyCapabilityRegistry.Serialize(reply)));
        return true;
    }
    private static readonly JsonSerializerOptions capabilityCommandJson = new(JsonSerializerDefaults.Web);
}
