namespace Kora.Core.Providers;

/// <summary>The desired service, independent of processing mode and execution authority.</summary>
public enum ProviderService
{
    /// <summary>No service selected.</summary>
    Unset,
    /// <summary>The local Ollama service.</summary>
    Local,
    /// <summary>GitHub Copilot.</summary>
    Copilot,
    /// <summary>Anthropic Claude.</summary>
    Claude,
    /// <summary>OpenAI Codex.</summary>
    Codex,
}
