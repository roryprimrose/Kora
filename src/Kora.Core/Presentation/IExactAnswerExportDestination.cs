namespace Kora.Core.Presentation;

/// <summary>Owns canonical destination identity until the one-shot write finishes.</summary>
public interface IExactAnswerExportDestination : IAsyncDisposable
{
    /// <summary>Gets the complete canonical destination shown in native review.</summary>
    string Path { get; }

    /// <summary>Stages, revalidates, publishes without replacement, and verifies exact bytes.</summary>
    Task<ExactAnswerExportOutcome> PublishAsync(AdmittedDetailContent source,
        Func<CancellationToken, Task> revalidate, Func<bool> admitted, CancellationToken token);
}
