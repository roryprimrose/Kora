namespace Kora.Core.Skills;

/// <summary>The last observed rename boundary; later durable effects may remain unconfirmed.</summary>
public enum OwnedSkillRenameEffect
{
    /// <summary>Private intent publication was attempted; inspect retained intent before retry.</summary>
    IntentMayExist,
    /// <summary>The complete destination was published; source retirement is not confirmed.</summary>
    DestinationPublished,
    /// <summary>Exact source retirement began and may be partial.</summary>
    SourceRetirementStarted,
    /// <summary>Native readback completed; subsequent admission or audit was not confirmed.</summary>
    NativeReadbackVerified,
}
