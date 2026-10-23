namespace Kora.Application.Dependencies;

/// <summary>An exact, separately reviewed data operation. Preparing data does not select or enable a runtime.</summary>
public enum SpeechComponentOperation
{
    /// <summary>Acquire missing data, or reuse already verified exact data.</summary>
    Prepare,
    /// <summary>Verify existing bytes and run the installed adapter's inert fixture without network access.</summary>
    Verify,
    /// <summary>Remove only the exact managed model data, not preferences or personal profiles.</summary>
    Remove,
}
