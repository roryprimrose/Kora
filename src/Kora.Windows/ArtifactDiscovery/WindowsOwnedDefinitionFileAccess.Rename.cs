using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Windows.Coordination;
using Kora.Windows.Storage;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Artifacts;

internal sealed partial class WindowsOwnedDefinitionFileAccess
{
    private const int MaximumRenameIntentBytes = 192 * 1024;
    private const string RenameIntentName = "instruction-rename.json";
    private static readonly JsonSerializerOptions RenameJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record RenameIntent(int Version, Guid Operation, string SourceName, string DestinationName,
        string DirectoryIdentity, string RootBindingIdentity, string SourceIdentity, string StagedIdentity, byte[] Original);
    internal Action<OwnedInstructionRenamePhase>? AfterRenamePhase { get; set; }
    internal Func<SafeFileHandle, uint> RenameDirectoryFlags { get; set; } = ReadRenameDirectoryFlags;
    private static string RenameIntentDirectory(IApplicationDataPaths paths) => Path.Combine(paths.LocalRoot, "OwnedInstructionRename");
    private static string RenameIntentPath(IApplicationDataPaths paths) => Path.Combine(RenameIntentDirectory(paths), RenameIntentName);
    private string RenameSlot(RenameIntent intent, bool original) =>
        Path.Combine(directory, $".instruction-rename-{intent.Operation:N}.{(original ? "original" : "proposed")}");

    internal static void RequireNoInstructionRename(IApplicationDataPaths paths, ArtifactKind kind, CancellationToken token)
    {
        if (kind != ArtifactKind.Instruction) { return; }
        token.ThrowIfCancellationRequested();
        using var probe = CreateFile(RenameIntentPath(paths), 0x80000000, 5, nint.Zero, 3, 0x00200000, nint.Zero);
        if (probe.IsInvalid)
        {
            if (Marshal.GetLastWin32Error() is 2 or 3) { return; }
            throw NativeFailure("Owned instruction rename recovery state is inaccessible; use and writes are withheld.");
        }
        throw new InvalidOperationException("An unfinished owned Instruction rename requires explicit Recover rename. No automatic replay, rollback, write or use is permitted.");
    }

    internal Task<OwnedInstructionSnapshot> RenameInstruction(OwnedInstructionRename proposal, Func<bool> eligible,
        Func<Func<OwnedInstructionSnapshot>, Task<OwnedInstructionSnapshot>> commit, CancellationToken token) =>
        Task.Run(async () => await commit(() =>
        {
            lock (gate) { return BeginInstructionRename(proposal, eligible, token); }
        }).ConfigureAwait(false), token);

    private OwnedInstructionSnapshot BeginInstructionRename(OwnedInstructionRename proposal, Func<bool> eligible, CancellationToken token)
    {
        RequireInstructionRenameKind();
        RequireNoInstructionRename(paths, kind, token);
        using var pinned = PinRoot(token);
        RequireRenameDirectory(pinned);
        using var journalRoot = PinDefinitionDirectory(RenameIntentDirectory(paths), true, token);
        using var source = OpenFile(Target(proposal.Source.Name), 0x80010000, 1);
        RequireRenameSource(proposal.Source, pinned, source, token);
        RequireRenameAbsent(Target(proposal.DestinationName));
        RequireEligible(eligible, token);
        var operation = Guid.NewGuid();
        var proposedPath = Path.Combine(directory, $".instruction-rename-{operation:N}.proposed");
        string stagedIdentity;
        using (var staged = CreatePrivateStaging(proposedPath))
        {
            VerifyFile(staged, proposedPath);
            stagedIdentity = WindowsFileIdentity.Read(staged);
            RandomAccess.Write(staged, proposal.Proposed.CopyBytes(), 0);
            RandomAccess.FlushToDisk(staged);
        }
        var intent = new RenameIntent(1, operation, proposal.Source.Name, proposal.DestinationName,
            proposal.Source.DirectoryIdentity, pinned.BindingIdentity, proposal.Source.FileIdentity, stagedIdentity, proposal.Source.Document.CopyBytes());
        var intentAttempted = false;
        try
        {
            RequireRenameSource(proposal.Source, pinned, source, token);
            RequireRenameAbsent(Target(proposal.DestinationName));
            RequireEligible(eligible, token);
            intentAttempted = true;
            WriteDefinitionFile(RenameIntentDirectory(paths), journalRoot, RenameIntentName,
                JsonSerializer.SerializeToUtf8Bytes(intent, RenameJson), false, eligible, token);
            // Reopen the durable intent before any identity move; the original is still pinned.
            using var journal = OpenFile(RenameIntentPath(paths), 0x80010000, 1);
            var read = ReadRenameIntent(journal, token);
            if (read != intent && !JsonSerializer.SerializeToUtf8Bytes(read, RenameJson).AsSpan()
                .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(intent, RenameJson)))
            { throw new IOException("Rename intent readback differs; the original has not been moved."); }
            AfterRenamePhase?.Invoke(OwnedInstructionRenamePhase.Prepared);
            return CompleteInstructionRename(intent, proposal, pinned, journal, source, eligible, token);
        }
        catch (Exception exception) when (intentAttempted && exception is not OutOfMemoryException)
        { throw new InstructionRenameUnconfirmedException(exception); }
        finally
        {
            if (!intentAttempted) { DeleteDefinitionFile(proposedPath, static () => true, CancellationToken.None); }
        }
    }

    internal Task<OwnedInstructionRenameRecovery?> ReadRenameRecovery(CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            RequireInstructionRenameKind();
            using var probe = CreateFile(RenameIntentPath(paths), 0x80000000, 5, nint.Zero, 3, 0x00200000, nint.Zero);
            if (probe.IsInvalid)
            {
                if (Marshal.GetLastWin32Error() is 2 or 3) { return null; }
                throw NativeFailure("Rename recovery data is inaccessible.");
            }
            using var pinned = PinRoot(token);
            RequireRenameDirectory(pinned);
            using var journalRoot = PinDefinitionDirectory(RenameIntentDirectory(paths), false, token);
            VerifyFile(probe, RenameIntentPath(paths));
            var intent = ReadRenameIntent(probe, token);
            return ObserveInstructionRename(intent, pinned, token);
        }
    }, token);

    internal Task<OwnedInstructionSnapshot> RecoverInstructionRename(OwnedInstructionRenameRecovery recovery, Func<bool> eligible,
        Func<Func<OwnedInstructionSnapshot>, Task<OwnedInstructionSnapshot>> commit, CancellationToken token) =>
        Task.Run(async () => await commit(() =>
        {
            lock (gate)
            {
                RequireInstructionRenameKind();
                using var pinned = PinRoot(token);
                RequireRenameDirectory(pinned);
                using var journalRoot = PinDefinitionDirectory(RenameIntentDirectory(paths), false, token);
                using var journal = OpenFile(RenameIntentPath(paths), 0x80010000, 1);
                var intent = ReadRenameIntent(journal, token);
                var observed = ObserveInstructionRename(intent, pinned, token);
                if (!string.Equals(observed.ObservationRevision, recovery.ObservationRevision, StringComparison.Ordinal)
                    || observed.Phase != recovery.Phase
                    || !string.Equals(observed.Proposal.DestinationName, recovery.Proposal.DestinationName, StringComparison.Ordinal)
                    || !string.Equals(observed.Proposal.Source.RootBindingIdentity, recovery.Proposal.Source.RootBindingIdentity, StringComparison.Ordinal)
                    || !string.Equals(observed.Proposal.Source.Revision, recovery.Proposal.Source.Revision, StringComparison.Ordinal)
                    || !string.Equals(observed.Proposal.Proposed.Definition.Digest, recovery.Proposal.Proposed.Definition.Digest, StringComparison.Ordinal))
                { throw new InvalidOperationException("The exact unfinished rename changed. Inspect recovery again; no retry or overwrite occurred."); }
                RequireEligible(eligible, token);
                try { return CompleteInstructionRename(intent, observed.Proposal, pinned, journal, null, eligible, token); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                { throw new InstructionRenameUnconfirmedException(exception); }
            }
        }).ConfigureAwait(false), token);

    private OwnedInstructionSnapshot CompleteInstructionRename(RenameIntent intent, OwnedInstructionRename proposal,
        Pinned pinned, SafeFileHandle journal, SafeFileHandle? pinnedSource, Func<bool> eligible, CancellationToken token)
    {
        var observed = ObserveInstructionRename(intent, pinned, token);
        if (observed.Phase == OwnedInstructionRenamePhase.Prepared)
        {
            using var reopened = pinnedSource is null ? OpenFile(Target(intent.SourceName), 0x80010000, 1) : null;
            var source = pinnedSource ?? reopened!;
            RequireRenameSource(proposal.Source, pinned, source, token);
            RequireRenameAbsent(Target(intent.DestinationName));
            RequireEligible(eligible, token);
            Rename(source, pinned.Last, Path.GetFileName(RenameSlot(intent, true)), false, token);
            AfterRenamePhase?.Invoke(OwnedInstructionRenamePhase.OriginalHeld);
        }
        observed = ObserveInstructionRename(intent, pinned, token);
        if (observed.Phase == OwnedInstructionRenamePhase.OriginalHeld)
        {
            using var staged = OpenFile(RenameSlot(intent, false), 0x80010000, 1);
            RequireExactRenameFile(staged, intent.StagedIdentity, proposal.Proposed.CopyBytes(), token);
            RequireRenameAbsent(Target(intent.SourceName));
            RequireRenameAbsent(Target(intent.DestinationName));
            RequireEligible(eligible, token);
            Rename(staged, pinned.Last, intent.DestinationName + extension, false, token);
            AfterRenamePhase?.Invoke(OwnedInstructionRenamePhase.Published);
        }
        observed = ObserveInstructionRename(intent, pinned, token);
        if (observed.Phase == OwnedInstructionRenamePhase.Published)
        {
            using var reopened = pinnedSource is null ? OpenFile(RenameSlot(intent, true), 0x80010000, 1) : null;
            var original = pinnedSource ?? reopened!;
            VerifyFile(original, RenameSlot(intent, true));
            RequireExactRenameFile(original, intent.SourceIdentity, intent.Original, token);
            RequireEligible(eligible, token);
            if (!SetFileInformationByHandle(original, 4, [1], 1))
            { throw NativeFailure("The retained original cleanup is unconfirmed."); }
            // A supplied original handle stays open until the caller returns. Close it before absence readback.
            original.Dispose();
            AfterRenamePhase?.Invoke(OwnedInstructionRenamePhase.CleanupPending);
        }
        var final = ObserveInstructionRename(intent, pinned, token);
        if (final.Phase != OwnedInstructionRenamePhase.CleanupPending)
        { throw new IOException("The complete rename is not confirmed. Recovery data is retained."); }
        RequireEligible(eligible, token);
        var snapshot = ReadCore(intent.DestinationName, pinned, token)
            ?? throw new IOException("Renamed instruction readback disappeared.");
        if (!string.Equals(snapshot.FileIdentity, intent.StagedIdentity, StringComparison.Ordinal)
            || !snapshot.Document.CopyBytes().AsSpan().SequenceEqual(proposal.Proposed.CopyBytes()))
        { throw new IOException("The renamed native identity or exact bytes changed during readback."); }
        RequireEligible(eligible, token);
        if (!SetFileInformationByHandle(journal, 4, [1], 1))
        { throw NativeFailure("Rename recovery metadata cleanup is unconfirmed."); }
        return new(snapshot.Name, snapshot.DirectoryIdentity, snapshot.FileIdentity, snapshot.Document)
        { CanonicalFileName = snapshot.CanonicalFileName, RootBindingIdentity = snapshot.RootBindingIdentity };
    }

    private OwnedInstructionRenameRecovery ObserveInstructionRename(RenameIntent intent, Pinned pinned, CancellationToken token)
    {
        RequireRenameDirectory(pinned);
        if (!string.Equals(intent.RootBindingIdentity, pinned.BindingIdentity, StringComparison.Ordinal)
            || !string.Equals(intent.DirectoryIdentity, WindowsFileIdentity.Read(pinned.Last), StringComparison.Ordinal))
        { throw new InvalidDataException("Rename source root or parent identities changed. No recovery publication is permitted."); }
        var source = new OwnedInstructionSnapshot(intent.SourceName, intent.DirectoryIdentity, intent.SourceIdentity,
            ArtifactFileDocument.Parse(intent.Original, ArtifactKind.Instruction, "kora-profile", intent.SourceName))
        { RootBindingIdentity = intent.RootBindingIdentity };
        var proposal = new OwnedInstructionRename(source, intent.DestinationName);
        var original = ReadRenameFile(Target(intent.SourceName), token);
        var held = ReadRenameFile(RenameSlot(intent, true), token);
        var staged = ReadRenameFile(RenameSlot(intent, false), token);
        var destination = ReadRenameFile(Target(intent.DestinationName), token);
        if (original is not null) { RequireExactRenameFile(original, intent.SourceIdentity, intent.Original); }
        if (held is not null) { RequireExactRenameFile(held, intent.SourceIdentity, intent.Original); }
        if (staged is not null) { RequireExactRenameFile(staged, intent.StagedIdentity, proposal.Proposed.CopyBytes()); }
        if (destination is not null) { RequireExactRenameFile(destination, intent.StagedIdentity, proposal.Proposed.CopyBytes()); }
        var phase = (original is not null, held is not null, staged is not null, destination is not null) switch
        {
            (true, false, true, false) => OwnedInstructionRenamePhase.Prepared,
            (false, true, true, false) => OwnedInstructionRenamePhase.OriginalHeld,
            (false, true, false, true) => OwnedInstructionRenamePhase.Published,
            (false, false, false, true) => OwnedInstructionRenamePhase.CleanupPending,
            _ => throw new InvalidDataException("Rename membership changed unexpectedly. Recovery is held; no original, destination or unrelated file will be overwritten."),
        };
        var revision = Convert.ToHexStringLower(SHA256.HashData(
            [.. JsonSerializer.SerializeToUtf8Bytes(intent, RenameJson), .. Encoding.UTF8.GetBytes(phase.ToString())]));
        return new(proposal, revision, phase);
    }

    private static RenameIntent ReadRenameIntent(SafeFileHandle handle, CancellationToken token)
    {
        RenameIntent intent;
        try
        {
            intent = JsonSerializer.Deserialize<RenameIntent>(ReadRenameBytes(handle, MaximumRenameIntentBytes, token), RenameJson)
                ?? throw new InvalidDataException("The rename intent is empty.");
        }
        catch (JsonException exception) { throw new InvalidDataException("The rename intent is malformed; recovery remains held.", exception); }
        if (intent.Version != 1 || intent.Operation == Guid.Empty || intent.Original is null
            || intent.Original.Length is 0 or > ArtifactDefinition.MaximumContentLength
            || !IsRenameIdentity(intent.DirectoryIdentity, 48) || !IsRenameIdentity(intent.SourceIdentity, 48)
            || !IsRenameIdentity(intent.StagedIdentity, 48) || !IsRenameIdentity(intent.RootBindingIdentity, 64))
        { throw new InvalidDataException("The bounded rename intent identity or version is invalid."); }
        OwnedInstructionNames.WriteName(intent.SourceName);
        OwnedInstructionNames.WriteName(intent.DestinationName);
        return intent;
    }

    private static bool IsRenameIdentity(string? value, int length) =>
        value is not null && value.Length == length && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static byte[] ReadRenameBytes(SafeFileHandle handle, int maximum, CancellationToken token)
    {
        var length = RandomAccess.GetLength(handle);
        if (length is <= 0 || length > maximum) { throw new InvalidDataException("Rename data is empty or exceeds its bounded capacity."); }
        var bytes = new byte[(int)length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            var count = RandomAccess.Read(handle, bytes.AsSpan(offset), offset);
            if (count == 0) { throw new IOException("Rename data changed during readback."); }
            offset += count;
        }
        return bytes;
    }

    private static DefinitionFile? ReadRenameFile(string path, CancellationToken token)
    {
        using var handle = CreateFile(path, 0x80000000, 5, nint.Zero, 3, 0x00200000, nint.Zero);
        if (handle.IsInvalid)
        {
            if (Marshal.GetLastWin32Error() == 2) { return null; }
            throw NativeFailure("Rename source, stage or destination is inaccessible.");
        }
        VerifyFile(handle, path);
        if (!string.Equals(Path.GetFileName(CoordinationNative.CanonicalPath(handle)), Path.GetFileName(path), StringComparison.Ordinal))
        { throw new InvalidDataException("A rename component has a case-alias filename."); }
        return new(ReadRenameBytes(handle, ArtifactDefinition.MaximumContentLength, token), WindowsFileIdentity.Read(handle), File.GetLastWriteTimeUtc(handle));
    }

    private static void RequireExactRenameFile(SafeFileHandle handle, string identity, byte[] bytes, CancellationToken token) =>
        RequireExactRenameFile(new(ReadRenameBytes(handle, ArtifactDefinition.MaximumContentLength, token),
            WindowsFileIdentity.Read(handle), File.GetLastWriteTimeUtc(handle)), identity, bytes);

    private static void RequireExactRenameFile(DefinitionFile actual, string identity, byte[] bytes)
    {
        if (!string.Equals(actual.Identity, identity, StringComparison.Ordinal) || !actual.Bytes.AsSpan().SequenceEqual(bytes))
        { throw new InvalidDataException("Rename component identity or exact bytes changed. No overwrite or automatic rollback is permitted."); }
    }

    private void RequireRenameSource(OwnedInstructionSnapshot expected, Pinned pinned, SafeFileHandle handle, CancellationToken token)
    {
        var actual = ReadHandle(expected.Name, pinned, handle, token);
        if (!string.Equals(Path.GetFileName(CoordinationNative.CanonicalPath(handle)), expected.CanonicalFileName, StringComparison.Ordinal)
            || !string.Equals(expected.RootBindingIdentity, pinned.BindingIdentity, StringComparison.Ordinal)
            || !string.Equals(expected.DirectoryIdentity, actual.DirectoryIdentity, StringComparison.Ordinal)
            || !string.Equals(expected.FileIdentity, actual.FileIdentity, StringComparison.Ordinal)
            || !expected.Document.CopyBytes().AsSpan().SequenceEqual(actual.Document.CopyBytes()))
        { throw new InvalidOperationException("The exact rename source or its parent binding changed. Reload and review again."); }
    }

    private static void RequireRenameAbsent(string path)
    {
        using var handle = CreateFile(path, 0x80, 7, nint.Zero, 3, 0x02200000, nint.Zero);
        if (!handle.IsInvalid || Marshal.GetLastWin32Error() != 2)
        { throw new InvalidOperationException("The new rename destination or old identity is occupied, aliased or unavailable. No replacement is permitted."); }
    }

    private void RequireInstructionRenameKind()
    {
        if (kind != ArtifactKind.Instruction) { throw new InvalidOperationException("Rename and its recovery support owned Instructions only."); }
    }

    private void RequireRenameDirectory(Pinned pinned)
    {
        if (RenameDirectoryFlags(pinned.Last) != 0)
        { throw new InvalidDataException("Owned Instruction rename requires a case-insensitive directory with known native case semantics. Case-sensitive or unknown flags cannot establish destination absence."); }
    }

    private static uint ReadRenameDirectoryFlags(SafeFileHandle directory)
    {
        var status = NtQueryInformationFile(directory, out _, out var flags, sizeof(uint), 71);
        if (status != 0)
        { throw new IOException("Owned Instruction directory case semantics are unavailable.", new Win32Exception(unchecked((int)status))); }
        return flags;
    }

    [LibraryImport("ntdll.dll")]
    private static partial uint NtQueryInformationFile(SafeFileHandle file, out IoStatus status, out uint data, uint length, int information);
}
