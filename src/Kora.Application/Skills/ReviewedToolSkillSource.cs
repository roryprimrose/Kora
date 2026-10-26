namespace Kora.Application.Skills;

/// <summary>Host-created source bindings; package content cannot choose a storage or authority domain.</summary>
internal abstract class ReviewedToolSkillSource : IDisposable
{
    internal abstract string Text { get; }
    internal abstract string AuditIdentity { get; }
    internal abstract bool IsCurrent { get; }
    public abstract void Dispose();
}
