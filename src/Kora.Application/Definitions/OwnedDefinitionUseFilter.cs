namespace Kora.Application.Definitions;

/// <summary>Filters observed catalogue use state, not current disk integrity or authority.</summary>
public enum OwnedDefinitionUseFilter
{
    /// <summary>Every enumerated owned filename.</summary>
    All,
    /// <summary>The exact mapped catalogue revision was observed enabled.</summary>
    UseEnabled,
    /// <summary>The exact mapped catalogue revision was observed held.</summary>
    ReviewRequired,
    /// <summary>No coherent exact catalogue revision or use state is available.</summary>
    UnavailableOrUnknown,
}
