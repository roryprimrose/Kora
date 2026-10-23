namespace Kora.Core.Dependencies;

/// <summary>The installed host adapter owns compatibility, inert functional checks and all runtime consumers.</summary>
public interface ISpeechComponentRuntime
{
    /// <summary>Checks the exact installed runtime/backend profile; unknown is incompatible.</summary>
    bool IsCompatible(SpeechComponentDefinition component);
    /// <summary>Excludes new consumers and joins existing model/capture users. Unknown/shared ownership returns no lease.</summary>
    ValueTask<IAsyncDisposable?> TryAcquireExclusiveAsync(SpeechComponentDefinition component, CancellationToken cancellationToken);
    /// <summary>Runs the exact adapter's inert local fixture without microphone, inference commands or preference changes.</summary>
    Task<bool> VerifyAsync(SpeechComponentDefinition component, CancellationToken cancellationToken);
}
