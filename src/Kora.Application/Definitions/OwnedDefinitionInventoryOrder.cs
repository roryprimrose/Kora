namespace Kora.Application.Definitions;

/// <summary>Explicit ordinal ordering of observed owned metadata before pagination.</summary>
public enum OwnedDefinitionInventoryOrder
{
    /// <summary>Canonical extensionless owned filename ID ascending, preserving the default inventory order.</summary>
    FileNameAscending,
    /// <summary>Canonical extensionless owned filename ID descending.</summary>
    FileNameDescending,
    /// <summary>Observed display name ascending, absent names last.</summary>
    DisplayNameAscending,
    /// <summary>Observed display name descending, absent names last.</summary>
    DisplayNameDescending,
}
