namespace Kora.Application.Configuration;

public sealed record AssistantNameConfigurationState(
    string? Name, long Revision, bool IsSaved, string? Recovery = null)
{
    public bool IsAvailable => Name is not null && Recovery is null;
}
