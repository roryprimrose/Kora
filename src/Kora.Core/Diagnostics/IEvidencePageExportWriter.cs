namespace Kora.Core.Diagnostics;

public interface IEvidencePageExportWriter
{
    Task<IEvidencePageExportDestination> SelectAsync(string path, CancellationToken token);
}
