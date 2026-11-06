namespace Kora.Core.Presentation;

/// <summary>Dedicated selected-destination authority, independent of definition writes and source reads.</summary>
public interface IExactAnswerExportWriter
{
    /// <summary>Admits a new local text destination without writing answer content.</summary>
    Task<IExactAnswerExportDestination> SelectAsync(string path, CancellationToken token);
}
