using System.Text;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Presentation;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.Presentation;

public sealed partial class WindowsExactAnswerExportWriter(IApplicationDataPaths paths,
    ILogger<WindowsExactAnswerExportWriter> logger,
    IExactAnswerExportCheckpoint? checkpoint = null) : IExactAnswerExportWriter
{
    public async Task<IExactAnswerExportDestination> SelectAsync(string path, CancellationToken token)
    {
        LocalFilePolicy.ValidatePath(path);
        var destination = await new WindowsCreateOnlyExportDestination(paths, Failure, checkpoint)
            .SelectAsync(path, token).ConfigureAwait(false);
        return new Destination(destination);
    }

    private sealed class Destination(WindowsCreateOnlyExportDestination.Destination destination) : IExactAnswerExportDestination
    {
        public string Path => destination.Path;
        public Task<ExactAnswerExportOutcome> PublishAsync(AdmittedDetailContent source,
            Func<CancellationToken, Task> revalidate, Func<bool> admitted, CancellationToken token)
        {
            if (source.Origin != DetailContentOrigin.RetainedAssistant)
            { throw new InvalidOperationException("Only a separately reviewed exact retained ASSISTANT answer can be exported."); }
            return destination.PublishAsync(new UTF8Encoding(false, true).GetBytes(source.Source), revalidate, admitted, token);
        }

        public ValueTask DisposeAsync() => destination.DisposeAsync();
    }
}
