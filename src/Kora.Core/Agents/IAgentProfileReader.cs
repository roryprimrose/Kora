namespace Kora.Core.Agents;

/// <summary>Native user-owned file selection; retains source ownership until disposed after quiescence.</summary>
public interface IAgentProfileReader
{
    /// <summary>Opens an existing canonical local .agent.md file without importing references.</summary>
    Task<IAgentProfileSelection> OpenAsync(string path, CancellationToken token);
}
