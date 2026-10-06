namespace Kora.Setup;

public interface IOptionalComponentSetup
{
    Task PrepareAsync(OptionalComponents components, IProgress<string> progress, CancellationToken cancellationToken);
}
