namespace Kora.Core.Skills;

public interface ISharedSkillSourceReader
{
    ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken);
    ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken);
    ValueTask<SharedSkillSnapshot> ReadReferencesAsync(SharedSkillSource source, SharedSkillSnapshot primary,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<SharedSkillSnapshot>(new InvalidOperationException("This source reader does not support separately consented Markdown references."));
}
