namespace Kora.Core.Diagnostics;

public interface IEvidencePageExportDestination : IAsyncDisposable
{
    string Path { get; }
    Task<EvidencePageExportOutcome> PublishAsync(EvidencePageExportSnapshot source,
        Func<CancellationToken, Task> revalidate, Func<bool> admitted, CancellationToken token);
}
