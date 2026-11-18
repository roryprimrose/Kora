using System.Text.Json;

using GitHub.Copilot;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Windows.Dependencies;

public sealed partial class CopilotSdkTurnRuntime
{
    public Task<CopilotTurnReply> RunSelectedAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ReadOnlyMemory<byte> context, CopilotCustomizationSelection selection,
        Func<CancellationToken, ValueTask<bool>> sourceCurrent,
        Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admitTransmission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(sourceCurrent);
        return RunCoreAsync(provenance, profile, context,
            static (_, _, _) => ValueTask.FromResult(new CapabilityReply(CapabilityOutcome.Denied, "selected-copilot-is-tool-free")),
            admitTransmission, null, cancellationToken, selection, sourceCurrent);
    }

#pragma warning disable GHCP001 // Pinned native ephemeral skill provider; no discovery roots or native tools.
    internal static CopilotSelectedSkillProvider? ConfigureSelection(SessionConfig configuration,
        CopilotCustomizationSelection selection, Func<CancellationToken, ValueTask<bool>> current,
        Func<IDisposable> enter, Func<HostActivity> continuation, CancellationToken turnToken)
    {
        CopilotSelectedSkillProvider? provider = selection.IsSkill ? new(selection, current, enter, continuation, turnToken) : null;
        configuration.EnableSkills = selection.IsSkill;
        configuration.SkillProvider = provider;
        configuration.IncludedBuiltinSkills = [];
        configuration.Agent = selection.IsSkill ? "kora-reviewed-skill" : selection.Name;
        configuration.CustomAgents =
        [
            new()
            {
                Name = configuration.Agent,
                Description = selection.Description,
                Prompt = selection.IsSkill
                    ? "Use only the one explicitly preloaded reviewed skill for the original host request. Return text only."
                    : selection.SourceText,
                Tools = [],
                Infer = false,
                Skills = selection.IsSkill ? [selection.Name] : [],
                McpServers = new Dictionary<string, McpServerConfig>(StringComparer.Ordinal),
            },
        ];
        return provider;
    }
#pragma warning restore GHCP001

    private static bool SelectedContentPresent(CopilotTransmission transmission,
        CopilotCustomizationSelection selection, CopilotSelectedSkillProvider? provider)
    {
        if (selection.IsSkill && provider?.WasRead != true) { return false; }
        using var document = JsonDocument.Parse(transmission.Body.ToArray());
        return Contains(document.RootElement);

        bool Contains(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!.Contains(selection.IsSkill
                ? selection.Instructions : selection.SourceText, StringComparison.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Any(Contains),
            JsonValueKind.Object => element.EnumerateObject().Any(property => Contains(property.Value)),
            _ => false,
        };
    }
}
