using Kora.Core.Voice;
using Kora.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Voice;

public sealed partial class BoundedMicrophoneCatalog
{
    private readonly Lock gate = new();
    private readonly Func<Task<MicrophoneCatalogSnapshot>> enumerate;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private Task<MicrophoneCatalogSnapshot>? pending;

    public BoundedMicrophoneCatalog(IVoiceRecognitionService voice,
        IWindowsPrivacyObservationService privacy, IMicrophoneAccessService access, ILogger logger)
        : this(() => Task.Run(() => new MicrophoneCatalogSnapshot(
            voice.GetMicrophones(), voice.GetDefaultMicrophone(), privacy.Refresh(), access.GetStatus())),
            TimeProvider.System, logger)
    {
    }

    internal BoundedMicrophoneCatalog(
        Func<Task<MicrophoneCatalogSnapshot>> enumerate, TimeProvider timeProvider, ILogger logger)
    {
        this.enumerate = enumerate;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public async Task<MicrophoneCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        Task<MicrophoneCatalogSnapshot> operation;
        lock (gate)
        {
            // Native enumeration cannot be forcibly interrupted. Never accumulate workers after timeout.
            if (pending is { IsCompleted: false })
            {
                throw new InvalidOperationException("A microphone refresh is still finishing. Retry Refresh devices later.");
            }
            operation = pending = enumerate();
        }
        _ = operation.ContinueWith(task => EnumerationFailed(logger, task.Exception!),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return await operation.WaitAsync(TimeSpan.FromSeconds(5), timeProvider, cancellationToken).ConfigureAwait(false);
    }
}
