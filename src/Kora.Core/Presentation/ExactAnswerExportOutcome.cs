namespace Kora.Core.Presentation;

/// <summary>Terminal effect of one create-only exact-answer export.</summary>
public enum ExactAnswerExportOutcome
{
    /// <summary>The new file was durably flushed and its exact bytes verified.</summary>
    Saved,
    /// <summary>No answer file was published.</summary>
    NotWritten,
    /// <summary>Cancellation completed before publication.</summary>
    Cancelled,
    /// <summary>A file may exist; neither success nor rollback is certified.</summary>
    Uncertain,
}
