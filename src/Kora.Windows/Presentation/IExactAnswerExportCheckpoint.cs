namespace Kora.Windows.Presentation;

/// <summary>Observes native transaction boundaries without receiving content or destination paths.</summary>
public interface IExactAnswerExportCheckpoint
{
    /// <summary>Joins the observer before proceeding across the specified durability boundary.</summary>
    Task ReachedAsync(ExactAnswerExportPhase phase, CancellationToken token);
}
