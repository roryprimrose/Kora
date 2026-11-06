namespace Kora.Core.Customizations;

/// <summary>The confirmed terminal outcome of one exact owned single-file deletion.</summary>
public enum CustomizationDeletionOutcome
{
    /// <summary>The user declined confirmation; no deletion was requested.</summary>
    Cancelled,
    /// <summary>The reviewed file was deleted and its absence verified.</summary>
    Deleted,
}
