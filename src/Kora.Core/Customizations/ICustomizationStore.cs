namespace Kora.Core.Customizations;

public interface ICustomizationStore
{
    Task<IReadOnlyList<CustomizationSource>> List(CancellationToken token);
    Task<CustomizationSource> Create(CustomizationKind kind, CustomizationScope scope, string name, CancellationToken token);
    Task<CustomizationSnapshot> Read(CustomizationSource source, CancellationToken token);
    Task<CustomizationSnapshot> Save(CustomizationSource source, CustomizationSnapshot? expected,
        IReadOnlyList<CustomizationFile> files, Func<bool> eligible,
        Func<Func<CustomizationSnapshot>, Task<CustomizationSnapshot>> commit, CancellationToken token);
    Task<CustomizationSnapshot> ReadRecovery(CustomizationSource source, CancellationToken token);
}
