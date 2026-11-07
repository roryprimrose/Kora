using Kora.Core.Dependencies;
using Kora.Core.KnowledgeSources;

namespace Kora.Application.Configuration;

public sealed class LocalKnowledgeSourceCaptureLimitsPreferences : IKnowledgeSourceCaptureLimitsPreferences
{
    private readonly IPreferenceStore store;
    public LocalKnowledgeSourceCaptureLimitsPreferences(IApplicationDataPaths paths) : this(new LocalPreferenceStore(paths)) { }
    internal LocalKnowledgeSourceCaptureLimitsPreferences(IPreferenceStore store) => this.store = store;

    public KnowledgeSourceCaptureLimits? Load(Guid sourceId)
    {
        if (store.ReadText(PendingName(sourceId), 8) is not null)
        { throw new InvalidDataException("Source capture limits have an unconfirmed write; refresh is unavailable until explicit recovery."); }
        return ReadBack(sourceId);
    }

    public KnowledgeSourceCaptureLimits? ReadBack(Guid sourceId) =>
        store.ReadText(Name(sourceId), 128) is { } text ? KnowledgeSourceCaptureLimits.Parse(text) : null;

    public void BeginWrite(Guid sourceId) => store.WriteText(PendingName(sourceId), "1");
    public void Save(Guid sourceId, KnowledgeSourceCaptureLimits? limits)
    {
        if (limits is null) { store.Delete(Name(sourceId)); }
        else { store.WriteText(Name(sourceId), limits.Serialize()); }
    }
    public void ConfirmWrite(Guid sourceId) => store.Delete(PendingName(sourceId));
    public void Remove(Guid sourceId)
    {
        store.Delete(Name(sourceId));
        store.Delete(PendingName(sourceId));
        if (Load(sourceId) is not null) { throw new InvalidDataException("Removed source numeric preferences remain."); }
    }

    private static string Name(Guid id) => "knowledge-capture-" + Require(id).ToString("N") + ".txt";
    private static string PendingName(Guid id) => "knowledge-capture-" + Require(id).ToString("N") + "-unconfirmed.txt";
    private static Guid Require(Guid id) => id != Guid.Empty ? id
        : throw new InvalidDataException("Capture preferences require an exact registered source identity.");
}
