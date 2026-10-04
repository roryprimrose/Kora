namespace Kora.Core.Dependencies;

public interface ILocalModelSetup
{
    Task InstallAsync(IProgress<LocalModelSetupProgress> progress, CancellationToken cancellationToken);
}
