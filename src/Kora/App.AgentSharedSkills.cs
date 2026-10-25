using Kora.Application.Agents;
using Microsoft.Extensions.DependencyInjection;

namespace Kora;

public sealed partial class App
{
    private AgentSkillReferenceResolver CreateAgentSkillReferenceResolver() =>
        ActivatorUtilities.CreateInstance<AgentSkillReferenceResolver>(Services);
}
