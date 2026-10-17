namespace Kora.Application.Skills;

/// <summary>Presentation ordering of already admitted immutable skill metadata, never authority or completeness.</summary>
public enum SkillCatalogueOrder
{
    /// <summary>Preserve the admitted catalogue's source order.</summary>
    Source = 0,

    /// <summary>Order known names ascending ordinal, then exact canonical identity ascending ordinal; unknown names last.</summary>
    NameAscending = 1,

    /// <summary>Order known names descending ordinal, then exact canonical identity ascending ordinal; unknown names last.</summary>
    NameDescending = 2,
}
