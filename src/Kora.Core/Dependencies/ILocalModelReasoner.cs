namespace Kora.Core.Dependencies;

public interface ILocalModelReasoner
{
    Task<LocalModelResponse> ReasonAsync(
        string request,
        LocalModelContext context,
        CancellationToken cancellationToken);
}
