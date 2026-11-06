using Kora.Core.Dependencies;

namespace Kora.Windows.Storage;

internal sealed partial class RestrictedStorageDirectory
{
    internal static RestrictedStorageDirectory ForOwnedSkills(IApplicationDataPaths paths) =>
        new(paths, paths.RoamingRoot, includeKeys: false, "Skills", includeArtifacts: false);

    internal static RestrictedStorageDirectory ForOwnedSkillRenames(IApplicationDataPaths paths) =>
        new(paths, paths.LocalRoot, includeKeys: false, "OwnedSkillRenames", includeArtifacts: false);

    internal void CreateOwnedSkillDirectory(string path)
    {
        Verify();
        VerifyOwnedPath(path);
        RejectReparseAncestors(Path.GetDirectoryName(path)!);
        CreateRestrictedDirectory(path, user);
        VerifyDirectory(path);
    }

    internal void VerifyOwnedSkillDirectory(string path)
    {
        VerifyOwnedPath(path);
        VerifyDirectory(path);
    }
}
