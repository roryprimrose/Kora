namespace Kora.Core.Maintenance;

public interface ICanonicalReleasePageOpener
{
    Task OpenAsync(ReleaseVersion version, CancellationToken cancellationToken);
}
