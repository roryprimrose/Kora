namespace Kora.Windows.Presentation;

/// <summary>Native durability boundaries for deterministic transaction checkpoints.</summary>
public enum ExactAnswerExportPhase
{
    /// <summary>No staging file exists.</summary>
    BeforeStaging,
    /// <summary>Staging bytes are flushed; final source and destination validation is still required.</summary>
    Staged,
    /// <summary>The create-only rename has completed.</summary>
    Published,
    /// <summary>Durable publication is about to be verified.</summary>
    Readback,
}
