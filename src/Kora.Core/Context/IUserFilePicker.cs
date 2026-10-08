namespace Kora.Core.Context;

public interface IUserFilePicker
{
    Task<string?> SelectAsync(CancellationToken cancellationToken);
}
