namespace Kora.Application.Skills;

/// <summary>Opens an existing native skill inspector without invoking or enabling definitions.</summary>
public interface ISkillReviewNavigation
{
    /// <summary>Opens or activates <paramref name="surface"/> only while <paramref name="eligible"/> remains current.</summary>
    /// <returns>Whether the requested native inspector was opened or activated.</returns>
    bool TryOpen(SkillReviewSurface surface, Func<bool> eligible);
}
