using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

public sealed partial class BoundedAudioOutputCatalog
{
    private readonly Lock gate = new();
    private readonly Func<Task<AudioOutputCatalogSnapshot>> enumerate;
    private readonly TimeProvider time;
    private readonly ILogger logger;
    private Task<AudioOutputCatalogSnapshot>? pending;

    public BoundedAudioOutputCatalog(IAudioOutputDeviceCatalog catalog, ILogger<BoundedAudioOutputCatalog> logger)
        : this(() => Task.Run(() => new AudioOutputCatalogSnapshot(
            catalog.GetOutputDevices(), catalog.GetDefaultOutputDevice())), TimeProvider.System, logger) { }

    internal BoundedAudioOutputCatalog(Func<Task<AudioOutputCatalogSnapshot>> enumerate, TimeProvider time, ILogger logger)
    {
        this.enumerate = enumerate;
        this.time = time;
        this.logger = logger;
    }

    public async Task<AudioOutputCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        Task<AudioOutputCatalogSnapshot> operation;
        lock (gate)
        {
            if (pending is { IsCompleted: false })
            {
                throw new InvalidOperationException("An output metadata refresh is still finishing. Retry later.");
            }
            operation = pending = enumerate();
        }
        _ = operation.ContinueWith(task => EnumerationFailed(logger, task.Exception!),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return await operation.WaitAsync(TimeSpan.FromSeconds(5), time, cancellationToken).ConfigureAwait(false);
    }
}
