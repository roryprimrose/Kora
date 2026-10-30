using System.Security.Cryptography;
using System.Text;

using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Diagnostics;

namespace Kora.Windows.Artifacts;

public sealed partial class WindowsCustomizationStore
{
    private sealed class RegisteredInstructionSource(InstructionOriginalRegistration proposal, string directoryIdentity,
        string directoryBinding, string fileIdentity)
    {
        internal InstructionOriginalRegistration Proposal { get; } = proposal;
        internal string DirectoryIdentity { get; } = directoryIdentity;
        internal string DirectoryBinding { get; } = directoryBinding;
        internal string FileIdentity { get; set; } = fileIdentity;
    }
    private InstructionOriginalRegistration? pendingInstructionRegistration;
    private RegisteredInstructionSource? registeredInstruction;

    public InstructionOriginalRegistration? RegisteredInstruction => Volatile.Read(ref registeredInstruction)?.Proposal;

    private static bool IsInstructionOriginal(CustomizationSource source) =>
        source is { Kind: CustomizationKind.Instruction, Scope: CustomizationScope.User };

    public bool IsInstructionRegistered(CustomizationSource source) =>
        Volatile.Read(ref registeredInstruction)?.Proposal.Source == source;

    public void WithdrawInstruction(CustomizationSource source)
    {
        var pending = Volatile.Read(ref pendingInstructionRegistration);
        if (pending?.Source == source) { Interlocked.CompareExchange(ref pendingInstructionRegistration, null, pending); }
        var current = Volatile.Read(ref registeredInstruction);
        if (current?.Proposal.Source == source) { Interlocked.CompareExchange(ref registeredInstruction, null, current); }
    }

    public Task<InstructionOriginalRegistration> PrepareInstructionRegistration(CustomizationSource source, CancellationToken token) =>
        Task.Run(() =>
        {
            lock (gate)
            {
                var target = ResolveInstructionOriginal(source);
                using var parent = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(target.Directory, false, token);
                var identity = WindowsOwnedDefinitionFileAccess.InspectInstructionOriginal(source.Location, token);
                var revision = RegistrationRevision(parent.BindingIdentity, identity);
                var proposal = new InstructionOriginalRegistration(source, revision);
                pendingInstructionRegistration = proposal;
                return proposal;
            }
        }, token);

    public Task RegisterInstruction(InstructionOriginalRegistration proposal, Func<bool> eligible, CancellationToken token) =>
        Task.Run(() =>
        {
            lock (gate)
            {
                if (!ReferenceEquals(pendingInstructionRegistration, proposal))
                { throw new InvalidOperationException("The exact registration proposal expired. Review this source again."); }
                pendingInstructionRegistration = null;
                var target = ResolveInstructionOriginal(proposal.Source);
                using var parent = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(target.Directory, false, token);
                var identity = WindowsOwnedDefinitionFileAccess.InspectInstructionOriginal(proposal.Source.Location, token);
                if (!string.Equals(RegistrationRevision(parent.BindingIdentity, identity), proposal.NativeRevision, StringComparison.Ordinal))
                { throw new InvalidOperationException("The source was replaced during registration. Select and review it again."); }
                RequireEligible(eligible, token);
                Interlocked.Exchange(ref registeredInstruction, new(proposal, parent.Identity, parent.BindingIdentity, identity));
                try { RequireEligible(eligible, token); }
                catch { WithdrawInstruction(proposal.Source); throw; }
            }
        }, token);

    private Target ResolveInstructionOriginal(CustomizationSource source)
    {
        var target = Resolve(source);
        if (!IsInstructionOriginal(source))
        { throw new InvalidDataException("Select one existing personal native v1 instruction original; other kinds and destinations are not registered by this operation."); }
        OwnedInstructionNames.WriteName(source.Name);
        if (!string.Equals(target.FileName, source.Name + ".instructions.md", StringComparison.Ordinal)
            || !WindowsProfileInstructionReader.SupportedLocation(source.Location, profileRoaming))
        { throw new InvalidDataException("The original must be one exact canonical personal Code or Code - Insiders instruction file."); }
        return target;
    }

    private RegisteredInstructionSource RequireInstructionRegistration(Target target, CancellationToken token)
    {
        var registration = Volatile.Read(ref registeredInstruction);
        if (registration?.Proposal.Source != target.Source)
        { throw new InvalidOperationException("Review registration of this exact personal instruction before reading or editing its original. Existing read-only registrations are not editing consent; Create Kora copy remains a separate route."); }
        using var parent = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(target.Directory, false, token);
        if (!string.Equals(parent.Identity, registration.DirectoryIdentity, StringComparison.Ordinal)
            || !string.Equals(parent.BindingIdentity, registration.DirectoryBinding, StringComparison.Ordinal))
        { throw new InvalidOperationException("The registered source directory was replaced. Withdraw and review registration again; the draft is retained."); }
        var identity = WindowsOwnedDefinitionFileAccess.InspectInstructionOriginal(target.Source.Location, token);
        if (!string.Equals(identity, registration.FileIdentity, StringComparison.Ordinal))
        { throw new InvalidOperationException("The registered original was replaced. Review its registration again, then compare/rebase or discard/reload; the dirty draft is retained."); }
        return registration;
    }

    private static string RegistrationRevision(string directory, string file) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(directory + "\0" + file)));

    private CustomizationSnapshot PublishInstructionOriginal(Target target, CustomizationSnapshot? expected,
        CustomizationFile[] files, Func<bool> eligible, CancellationToken token)
    {
        using var activity = HostActivity.Current is null ? null : HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
        try
        {
            var result = PublishInstructionOriginalCore(target, expected, files, eligible, token);
            activity?.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException) { activity?.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity?.Complete(HostOperationOutcome.Failed); throw; }
    }

    private CustomizationSnapshot PublishInstructionOriginalCore(Target target, CustomizationSnapshot? expected,
        CustomizationFile[] files, Func<bool> eligible, CancellationToken token)
    {
        var registration = RequireInstructionRegistration(target, token);
        if (expected is null || expected.Source != target.Source || expected.RecoveryDetails is not null)
        { throw new InvalidDataException("Original editing requires the exact inspected base; create, delete, rename and transaction recovery are not approved."); }
        var proposed = ArtifactFileDocument.Parse(files.Single().CopyBytes(), ArtifactKind.Instruction,
            "profile-instruction", target.Source.Name);
        proposed.RequireProfileInstructionSource();
        using var parent = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(target.Directory, false, token);
        bool Current() => ReferenceEquals(Volatile.Read(ref registeredInstruction), registration) && eligible();
        void Compare()
        {
            RequireEligible(Current, token);
            var observed = Capture(target, token);
            WindowsOwnedDefinitionFileAccess.InspectInstructionOriginal(target.Source.Location, token);
            if (!string.Equals(observed.DirectoryIdentity, registration.DirectoryIdentity, StringComparison.Ordinal)
                || !string.Equals(observed.Revision, expected.Revision, StringComparison.Ordinal))
            { throw new InstructionOriginalConflictException(); }
        }
        Compare();
        var before = expected.Files.Single();
        if (before.CopyBytes().AsSpan().SequenceEqual(proposed.CopyBytes())) { return expected; }
        var published = false;
        try
        {
            var identity = WindowsOwnedDefinitionFileAccess.WriteDefinitionFile(target.Directory, parent,
                target.FileName, proposed.CopyBytes(), true, Current, token, () =>
                {
                    BeforeInstructionFinalComparison?.Invoke();
                    Compare();
                    AfterInstructionFinalComparison?.Invoke();
                    RequireEligible(Current, token);
                }, target.Source.Location, () => published = true);
            AfterComponentPublished?.Invoke(target.FileName);
            RequireEligible(Current, token);
            var observed = Capture(target, token);
            var actual = observed.Files.Single();
            if (!string.Equals(observed.DirectoryIdentity, registration.DirectoryIdentity, StringComparison.Ordinal)
                || !string.Equals(actual.Identity, identity, StringComparison.Ordinal)
                || !actual.Bytes.AsSpan().SequenceEqual(proposed.CopyBytes()))
            { throw new IOException("Original publication readback is unconfirmed."); }
            registration.FileIdentity = identity;
            return Expose(target, observed);
        }
        catch (Exception exception) when (published && exception is not OutOfMemoryException)
        {
            WithdrawInstruction(target.Source);
            throw new InstructionPublicationUnconfirmedException(token.IsCancellationRequested, exception);
        }
    }

    internal Action? AfterInstructionFinalComparison { get; set; }
    internal Action? BeforeInstructionFinalComparison { get; set; }
}