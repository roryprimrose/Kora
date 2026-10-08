namespace Kora.Core.Context;

public interface ILocalFileInspector
{
    Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken);
}
