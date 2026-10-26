using Kora.Core.Skills;

namespace Kora.Application.Skills;

internal sealed class SharedToolSkillSource(SharedSkillSource registration, SharedSkillSnapshot snapshot)
    : ReviewedToolSkillSource
{
    internal SharedSkillSource Registration { get; } = registration;
    internal SharedSkillSnapshot Snapshot { get; } = snapshot;
    internal override string Text => Snapshot.Text!;
    internal override string AuditIdentity => $"shared-revision.{Snapshot.SourceId:N}.{Snapshot.RevisionDigest}";
    internal override bool IsCurrent => true;
    public override void Dispose() { }
}
