namespace Kora.Core.Skills;

public sealed class BundledFixedScript
{
    public BundledFixedScript(SkillPackageSnapshot package, FixedSessionAction action)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!Enum.IsDefined(action)) { throw new ArgumentOutOfRangeException(nameof(action)); }
        Package = package;
        Action = action;
    }

    public SkillPackageSnapshot Package { get; }
    public FixedSessionAction Action { get; }
}