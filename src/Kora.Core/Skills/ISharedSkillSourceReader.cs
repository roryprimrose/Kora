namespace Kora.Core.Skills;

public interface ISharedSkillSourceReader
{
    ValueTask<SharedSkillSource> SelectAsync(string selectedRoot, CancellationToken cancellationToken);
    ValueTask<SharedSkillCatalogue> DiscoverAsync(SharedSkillSource source, CancellationToken cancellationToken);
    /// <summary>Binds one native selected file to an exact existing registration without reading guidance.</summary>
    ValueTask<SharedSkillFileBinding> BindFileAsync(IReadOnlyList<SharedSkillSource> registered,
        string selectedFile, CancellationToken cancellationToken) =>
        ValueTask.FromException<SharedSkillFileBinding>(new InvalidOperationException("Exact registered single-file selection is unavailable."));
    /// <summary>Reads only the exact bound file, refusing changed native identity, revision or package inventory.</summary>
    ValueTask<SharedSkillSnapshot> ReadFileAsync(SharedSkillFileBinding binding, CancellationToken cancellationToken) =>
        ValueTask.FromException<SharedSkillSnapshot>(new InvalidOperationException("Exact registered single-file reading is unavailable."));
    ValueTask<SharedSkillSnapshot> ReadReferencesAsync(SharedSkillSource source, SharedSkillSnapshot primary,
        CancellationToken cancellationToken) =>
        ValueTask.FromException<SharedSkillSnapshot>(new InvalidOperationException("This source reader does not support separately consented Markdown references."));
}
