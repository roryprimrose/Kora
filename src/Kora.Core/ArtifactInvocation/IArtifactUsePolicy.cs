namespace Kora.Core.Artifacts;

public interface IArtifactUsePolicy
{
    event EventHandler? Changed;
    long Revision { get; }
    bool IsDisabled(string identity);
}
