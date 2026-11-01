using Kora.Core.Artifacts;
using Kora.Core.Customizations;

namespace Kora.Application.Definitions;

public sealed class ProfileInstructionEditorWorkflow(CustomizationManagementService management)
{
    public InstructionOriginalRegistration? RegisteredInstruction => management.RegisteredInstruction;

    public bool IsCurrent(InstructionOriginalRegistration registration) =>
        ReferenceEquals(RegisteredInstruction, registration) && management.IsInstructionRegistered(registration.Source);

    public async Task<CustomizationSnapshot> Open(InstructionOriginalRegistration registration,
        Func<bool> eligible, CancellationToken token)
    {
        RequireCurrent(registration, eligible, token);
        var snapshot = await management.Read(registration.Source, false,
            () => eligible() && IsCurrent(registration), token).ConfigureAwait(false);
        RequireCurrent(registration, eligible, token);
        _ = Document(registration, snapshot);
        return snapshot;
    }

    public static ArtifactFileDocument PrepareSave(InstructionOriginalRegistration registration,
        CustomizationSnapshot original, string draft)
    {
        _ = Document(registration, original);
        var file = original.Files.Single().WithText(draft);
        var proposed = ArtifactFileDocument.Parse(file.CopyBytes(), ArtifactKind.Instruction,
            "profile-instruction", original.Source.Name);
        proposed.RequireProfileInstructionSource();
        return proposed;
    }

    public async Task<ProfileInstructionSaveOutcome> Save(InstructionOriginalRegistration registration,
        CustomizationSnapshot original, string draft, IProfileInstructionMutationReview review,
        Func<bool> eligible, CancellationToken token)
    {
        RequireCurrent(registration, eligible, token);
        var proposed = PrepareSave(registration, original, draft);
        var files = new[] { new CustomizationFile(original.Source.PrimaryFile, proposed.CopyBytes()) };
        var expected = original;
        bool Current() => eligible() && IsCurrent(registration);
        while (true)
        {
            try
            {
                var saved = await management.Save(registration.Source, expected, files,
                    admitted => review.Confirm(expected, proposed, admitted), Current, token).ConfigureAwait(false);
                RequireCurrent(registration, eligible, token);
                if (saved is null) { return new(false, false, original); }
                _ = Document(registration, saved);
                return new(true, false, saved);
            }
            catch (InstructionOriginalConflictException)
            {
                var observed = await Open(registration, eligible, token).ConfigureAwait(false);
                var choice = await review.Conflict(observed, proposed, token).ConfigureAwait(false);
                RequireCurrent(registration, eligible, token);
                if (choice == InstructionConflictChoice.Reload)
                { return new(false, true, await Open(registration, eligible, token).ConfigureAwait(false)); }
                if (choice != InstructionConflictChoice.Overwrite) { return new(false, false, original); }
                expected = observed;
            }
        }
    }

    private static ArtifactFileDocument Document(InstructionOriginalRegistration registration, CustomizationSnapshot snapshot)
    {
        if (registration.Source is not { Kind: CustomizationKind.Instruction, Scope: CustomizationScope.User }
            || snapshot.Source != registration.Source || snapshot.RecoveryDetails is not null)
        { throw new InvalidDataException("Open exactly one already registered personal instruction original; other sources, kinds and recovery transactions are unavailable."); }
        var document = ArtifactFileDocument.Parse(snapshot.Files.Single().CopyBytes(), ArtifactKind.Instruction,
            "profile-instruction", snapshot.Source.Name);
        document.RequireProfileInstructionSource();
        return document;
    }

    private void RequireCurrent(InstructionOriginalRegistration registration, Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!eligible() || !IsCurrent(registration))
        { throw new InvalidOperationException("The selected source registration, read consent or editor admission expired. No new registration is granted here. Review the exact source in Customizations, then explicitly Reload or reopen; the draft is retained."); }
    }
}
