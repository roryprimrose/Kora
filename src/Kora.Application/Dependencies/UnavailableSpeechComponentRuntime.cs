using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

/// <summary>No optional input backend is selected by D-002 or registered in production.</summary>
public sealed class UnavailableSpeechComponentRuntime : ISpeechComponentRuntime
{
    public bool IsCompatible(SpeechComponentDefinition component) => false;

    public ValueTask<IAsyncDisposable?> TryAcquireExclusiveAsync(SpeechComponentDefinition component,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IAsyncDisposable?>(null);
    }

    public Task<bool> VerifyAsync(SpeechComponentDefinition component, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}
