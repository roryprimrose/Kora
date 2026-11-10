namespace Kora.Core.KnowledgeSources;

public interface IKnowledgeSourceCaptureLimitsPreferences
{
    KnowledgeSourceCaptureLimits? Load(Guid sourceId);
    KnowledgeSourceCaptureLimits? ReadBack(Guid sourceId);
    void BeginWrite(Guid sourceId);
    void Save(Guid sourceId, KnowledgeSourceCaptureLimits? limits);
    void ConfirmWrite(Guid sourceId);
    void Remove(Guid sourceId);
}
