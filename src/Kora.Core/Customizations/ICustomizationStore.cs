namespace Kora.Core.Customizations;

public interface ICustomizationStore
{
    /// <summary>Inspects only native identity metadata for one selected personal instruction original.</summary>
    Task<InstructionOriginalRegistration> PrepareInstructionRegistration(CustomizationSource source, CancellationToken token);
    /// <summary>Registers the exact reviewed source for this host lifetime, without approving a mutation.</summary>
    Task RegisterInstruction(InstructionOriginalRegistration proposal, Func<bool> eligible, CancellationToken token);
    /// <summary>Withdraws exact-source registration without deleting any content.</summary>
    void WithdrawInstruction(CustomizationSource source);
    /// <summary>Observes whether this host currently holds an exact-source registration.</summary>
    bool IsInstructionRegistered(CustomizationSource source);
    Task<IReadOnlyList<CustomizationSource>> List(CancellationToken token);
    Task<CustomizationSource> Create(CustomizationKind kind, CustomizationScope scope, string name, CancellationToken token);
    Task<CustomizationSnapshot> Read(CustomizationSource source, CancellationToken token);
    Task<CustomizationSnapshot> Save(CustomizationSource source, CustomizationSnapshot? expected,
        IReadOnlyList<CustomizationFile> files, Func<bool> eligible,
        Func<Func<CustomizationSnapshot>, Task<CustomizationSnapshot>> commit, CancellationToken token);
    Task<CustomizationSnapshot> ReadRecovery(CustomizationSource source, CancellationToken token);
}