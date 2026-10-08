namespace Kora.Core.Context;

public interface ILocalFileSelection : IDisposable
{
    LocalFileMetadata Metadata { get; }
    Task<byte[]> ReadAsync(CancellationToken cancellationToken);
}
