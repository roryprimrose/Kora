namespace Kora.Core.Skills;

public interface ISharedSkillSourceReader
{
    ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken);
    ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken);
}
