using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Presentation;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.Presentation;

public sealed class WindowsEvidencePageExportWriter(IApplicationDataPaths paths, ILogger<WindowsEvidencePageExportWriter> logger,
    IExactAnswerExportCheckpoint? checkpoint = null) : IEvidencePageExportWriter
{
    public async Task<IEvidencePageExportDestination> SelectAsync(string path, CancellationToken token)
    {
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Evidence export requires a new JSON file."); }
        var destination = await new WindowsCreateOnlyExportDestination(paths,
            (error, published) => WindowsCreateOnlyExportDestination.ReportFailure(logger, error, published), checkpoint)
            .SelectAsync(path, token).ConfigureAwait(false);
        return new Destination(destination);
    }

    private sealed class Destination(WindowsCreateOnlyExportDestination.Destination destination) : IEvidencePageExportDestination
    {
        public string Path => destination.Path;
        public async Task<EvidencePageExportOutcome> PublishAsync(EvidencePageExportSnapshot source,
            Func<CancellationToken, Task> revalidate, Func<bool> admitted, CancellationToken token) =>
            await destination.PublishAsync(source.CopyBytes(), revalidate, admitted, token).ConfigureAwait(false) switch
            {
                ExactAnswerExportOutcome.Saved => EvidencePageExportOutcome.Saved,
                ExactAnswerExportOutcome.Cancelled => EvidencePageExportOutcome.Cancelled,
                ExactAnswerExportOutcome.NotWritten => EvidencePageExportOutcome.NotWritten,
                _ => EvidencePageExportOutcome.Uncertain,
            };

        public ValueTask DisposeAsync() => destination.DisposeAsync();
    }
}
