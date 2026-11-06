namespace Kora.Core.Skills;

/// <summary>Host-only native identities for one registered single-file package. Contains no guidance or authority.</summary>
public sealed record SharedSkillFileBinding(SharedSkillSource Source, string RelativeFile,
    string PackageIdentity, string FileIdentity, long LastWriteTicks);
