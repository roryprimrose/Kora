namespace Kora.Core.Configuration;

public interface IArtifactDirectoryPreferences
{
    ArtifactDirectorySettings Load();

    void Save(ArtifactDirectorySettings settings);
}
