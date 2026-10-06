namespace Kora.Core.Configuration;

public sealed record ModelExecutionSettings(
    bool LocalModelsEnabled,
    bool HostedModelsEnabled)
{
    public static ModelExecutionSettings Default { get; } = new(
        LocalModelsEnabled: true,
        HostedModelsEnabled: false);
}
