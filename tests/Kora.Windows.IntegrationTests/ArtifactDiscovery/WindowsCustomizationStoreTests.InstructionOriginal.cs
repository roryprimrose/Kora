using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Windows.Artifacts;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsCustomizationStoreTests
{
    private async Task<CustomizationSource> Original(WindowsCustomizationStore store, byte[]? bytes = null)
    {
        var directory = Path.Combine(root, "editor-profile", "Code", "User", "prompts");
        var userDirectory = Path.GetDirectoryName(directory)!;
        Directory.CreateDirectory(Path.GetDirectoryName(userDirectory)!);
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidOperationException("The private fixture owner is unavailable.");
        RestrictedStorageDirectory.CreateRestrictedDirectory(userDirectory, owner);
        RestrictedStorageDirectory.CreateRestrictedDirectory(directory, owner);
        var path = Path.Combine(directory, "example.instructions.md");
        WindowsOwnedInstructionStoreTests.WriteOwnedFile(path,
            bytes ?? Encoding.UTF8.GetBytes(CustomizationLayout.Template(CustomizationKind.Instruction, "example")));
        // Inventory also pins the editor User directory as an MCP root.
        foreach (var inventoryDirectory in new[] { userDirectory, directory })
        {
            var security = new DirectoryInfo(inventoryDirectory).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            security.GetOwner(typeof(SecurityIdentifier)).Should().Be(owner);
            security.AreAccessRulesProtected.Should().BeTrue();
        }
        new FileInfo(path).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier))
            .Should().Be(owner);
        return (await store.List(TestContext.Current.CancellationToken)).Single(source =>
            source.Scope == CustomizationScope.User && source.Kind == CustomizationKind.Instruction);
    }

    private static async Task Register(WindowsCustomizationStore store, CustomizationSource source)
    {
        var proposal = await store.PrepareInstructionRegistration(source, TestContext.Current.CancellationToken);
        await store.RegisterInstruction(proposal, () => true, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\r\n")]
    [InlineData(true, "\r")]
    public async Task RegisteredOriginalRoundtripsExactSourceBytesAndSecurityWithoutOwnedCopyOrJournal(bool bom, string newline)
    {
        var store = Create();
        var text = "---\nname: 'example'\nkora-format: 1\ndescription: \"Original description\"\nattribution: 'Original author'\n---\n# Heading\nExact original.\n"
            .Replace("\n", newline, StringComparison.Ordinal);
        var original = ArtifactFileDocument.Encode(text, bom);
        var source = await Original(store, original);
        File.SetAttributes(source.Location, File.GetAttributes(source.Location) | FileAttributes.NotContentIndexed);
        var attributes = File.GetAttributes(source.Location);
        var creation = File.GetCreationTimeUtc(source.Location);
        var security = new FileInfo(source.Location).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
        store.IsInstructionRegistered(source).Should().BeFalse();
        await ((Func<Task>)(() => store.Read(source, TestContext.Current.CancellationToken))).Should().ThrowAsync<InvalidOperationException>();
        await Register(store, source);
        var before = await store.Read(source, TestContext.Current.CancellationToken);
        before.Files.Single().CopyBytes().Should().Equal(original);
        var unchanged = await Save(store, source, before, before.Files.Single());
        unchanged.Should().BeSameAs(before);
        var proposed = before.Files.Single().WithText(text.Replace("Exact original.", "Exact changed.", StringComparison.Ordinal));
        var saved = await Save(store, source, before, proposed);
        saved.Revision.Should().NotBe(before.Revision);
        (await File.ReadAllBytesAsync(source.Location, TestContext.Current.CancellationToken)).Should().Equal(proposed.CopyBytes());
        (await store.Read(source, TestContext.Current.CancellationToken)).Files.Single().CopyBytes().Should().Equal(proposed.CopyBytes());
        new FileInfo(source.Location).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access).Should().Be(security);
        File.GetAttributes(source.Location).Should().Be(attributes);
        File.GetCreationTimeUtc(source.Location).Should().Be(creation);
        Directory.Exists(Path.Combine(root, "roaming", "Instructions")).Should().BeFalse();
        Directory.Exists(Path.Combine(root, "local", "DefinitionTransactions")).Should().BeFalse();
        Directory.EnumerateFiles(Path.GetDirectoryName(source.Location)!, "*.tmp").Should().BeEmpty();
        store.WithdrawInstruction(source);
        store.IsInstructionRegistered(source).Should().BeFalse();
        await ((Func<Task>)(() => store.Read(source, TestContext.Current.CancellationToken))).Should().ThrowAsync<InvalidOperationException>();
        (await File.ReadAllBytesAsync(source.Location, TestContext.Current.CancellationToken)).Should().Equal(proposed.CopyBytes());
    }

    [Theory]
    [InlineData("forged")]
    [InlineData("superseded")]
    [InlineData("replaced")]
    [InlineData("withdrawn")]
    [InlineData("revoked")]
    public async Task RegistrationBindsHostProposalAndFreshExactNativeOriginalWithoutPublishingBytes(string state)
    {
        var store = Create();
        var source = await Original(store);
        var original = await File.ReadAllBytesAsync(source.Location, TestContext.Current.CancellationToken);
        var proposal = await store.PrepareInstructionRegistration(source, TestContext.Current.CancellationToken);
        if (state is "forged") { proposal = proposal with { NativeRevision = "forged" }; }
        if (state is "superseded") { await store.PrepareInstructionRegistration(source, TestContext.Current.CancellationToken); }
        if (state is "withdrawn") { store.WithdrawInstruction(source); }
        if (state is "replaced")
        {
            File.Move(source.Location, source.Location + ".previous");
            WindowsOwnedInstructionStoreTests.WriteOwnedFile(source.Location, original);
        }
        await ((Func<Task>)(() => store.RegisterInstruction(proposal, () => state is not "revoked", TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<InvalidOperationException>();
        store.IsInstructionRegistered(source).Should().BeFalse();
        (await File.ReadAllBytesAsync(source.Location, TestContext.Current.CancellationToken)).Should().Equal(original);
        Directory.Exists(Path.Combine(root, "local", "DefinitionTransactions")).Should().BeFalse();
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("replaced")]
    [InlineData("staging-replaced")]
    [InlineData("revoked")]
    [InlineData("registration-withdrawn")]
    [InlineData("writable-sharing")]
    [InlineData("late-writable-sharing")]
    public async Task OriginalConflictsAndRevocationRetainExactExternalBytesAndForbidPublication(string state)
    {
        var store = Create();
        var source = await Original(store);
        await Register(store, source);
        var before = await store.Read(source, TestContext.Current.CancellationToken);
        var proposed = before.Files.Single().WithText(before.Files.Single().Text + "\nDraft only.");
        var external = before.Files.Single().CopyBytes();
        FileStream? writer = null;
        try
        {
            if (state is "changed")
            {
                external = before.Files.Single().WithText(before.Files.Single().Text + "\nExternal.").CopyBytes();
                await File.WriteAllBytesAsync(source.Location, external, TestContext.Current.CancellationToken);
            }
            if (state is "replaced")
            {
                File.Move(source.Location, source.Location + ".previous");
                WindowsOwnedInstructionStoreTests.WriteOwnedFile(source.Location, external);
            }
            if (state is "staging-replaced")
            {
                store.BeforeInstructionFinalComparison = () =>
                {
                    File.Move(source.Location, source.Location + ".previous");
                    WindowsOwnedInstructionStoreTests.WriteOwnedFile(source.Location, external);
                };
            }
            if (state is "registration-withdrawn") { store.WithdrawInstruction(source); }
            if (state is "writable-sharing") { writer = new(source.Location, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }
            if (state is "late-writable-sharing")
            {
                store.AfterInstructionFinalComparison = () => writer = new(source.Location, FileMode.Open,
                    FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            }
            var save = () => store.Save(source, before, [proposed], () => state is not "revoked",
                operation => Task.FromResult(operation()), TestContext.Current.CancellationToken);
            await save.Should().ThrowAsync<Exception>();
        }
        finally { writer?.Dispose(); }
        (await File.ReadAllBytesAsync(source.Location, TestContext.Current.CancellationToken)).Should().Equal(external);
        proposed.Text.Should().EndWith("Draft only.");
        Directory.EnumerateFiles(Path.GetDirectoryName(source.Location)!, "*.tmp").Should().BeEmpty();
        Directory.Exists(Path.Combine(root, "local", "DefinitionTransactions")).Should().BeFalse();
    }

    [Fact]
    public async Task RepeatedChangedBaseNeedsAnotherExplicitConflictDecision()
    {
        var store = Create();
        var source = await Original(store);
        await Register(store, source);
        var first = await store.Read(source, TestContext.Current.CancellationToken);
        var draft = first.Files.Single().WithText(first.Files.Single().Text + "\nRetained draft.");
        for (var change = 1; change <= 2; change++)
        {
            await File.WriteAllBytesAsync(source.Location,
                first.Files.Single().WithText(first.Files.Single().Text + "\nExternal " + change).CopyBytes(), TestContext.Current.CancellationToken);
            await ((Func<Task>)(() => Save(store, source, first, draft))).Should().ThrowAsync<InvalidOperationException>();
            var reviewedExternal = await store.Read(source, TestContext.Current.CancellationToken);
            reviewedExternal.Revision.Should().NotBe(first.Revision);
            first = reviewedExternal;
        }
        var saved = await Save(store, source, first, draft);
        saved.Files.Single().CopyBytes().Should().Equal(draft.CopyBytes());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostPublicationFailureOrWithdrawalIsExplicitlyUnconfirmedAndRetiresRegistration(bool withdraw)
    {
        var store = Create();
        var source = await Original(store);
        await Register(store, source);
        var before = await store.Read(source, TestContext.Current.CancellationToken);
        var proposed = before.Files.Single().WithText(before.Files.Single().Text + "\nPublished but held.");
        store.AfterComponentPublished = _ =>
        {
            if (withdraw) { store.WithdrawInstruction(source); }
            else { throw new IOException("Readback unavailable"); }
        };
        await ((Func<Task>)(() => Save(store, source, before, proposed))).Should().ThrowAsync<InstructionPublicationUnconfirmedException>();
        (await File.ReadAllBytesAsync(source.Location, TestContext.Current.CancellationToken)).Should().Equal(proposed.CopyBytes());
        store.IsInstructionRegistered(source).Should().BeFalse();
        await ((Func<Task>)(() => store.Read(source, TestContext.Current.CancellationToken))).Should().ThrowAsync<InvalidOperationException>();
        Directory.EnumerateFiles(Path.GetDirectoryName(source.Location)!, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("version")]
    [InlineData("metadata")]
    [InlineData("scope")]
    [InlineData("unicode")]
    [InlineData("oversized")]
    [InlineData("header")]
    public async Task UnsupportedOrMalformedOriginalIsRefusedWithoutRewrite(string state)
    {
        var store = Create();
        var text = CustomizationLayout.Template(CustomizationKind.Instruction, "example");
        var bytes = state switch
        {
            "version" => Encoding.UTF8.GetBytes(text.Replace("kora-format: 1", "kora-format: 2", StringComparison.Ordinal)),
            "metadata" => Encoding.UTF8.GetBytes(text.Replace("kora-format: 1", "hooks: run\nkora-format: 1", StringComparison.Ordinal)),
            "scope" => Encoding.UTF8.GetBytes(text.Replace("kora-format: 1", "applyTo: **\nkora-format: 1", StringComparison.Ordinal)),
            "unicode" => [0xff, 0xff],
            "oversized" => new byte[65537],
            _ => Encoding.UTF8.GetBytes(text.Replace("kora-format: 1", new string('x', 4097) + ": 1\nkora-format: 1", StringComparison.Ordinal)),
        };
        var source = await Original(store, bytes);
        await Register(store, source);
        await ((Func<Task>)(() => store.Read(source, TestContext.Current.CancellationToken))).Should().ThrowAsync<InvalidDataException>();
        (await File.ReadAllBytesAsync(source.Location, TestContext.Current.CancellationToken)).Should().Equal(bytes);
    }

    [Theory]
    [InlineData("hardlink")]
    [InlineData("readonly")]
    [InlineData("hidden")]
    [InlineData("alias")]
    [InlineData("named-stream")]
    public async Task UnsafeNativeOriginalCannotAcquireRegistration(string state)
    {
        var store = Create();
        var source = await Original(store);
        if (state is "hardlink")
        {
            CreateHardLink(source.Location + ".alias", source.Location, nint.Zero).Should().BeTrue();
        }
        if (state is "readonly") { File.SetAttributes(source.Location, FileAttributes.ReadOnly); }
        if (state is "hidden") { File.SetAttributes(source.Location, FileAttributes.Hidden); }
        if (state is "named-stream")
        { await File.WriteAllTextAsync(source.Location + ":notes", "Unrelated original stream.", TestContext.Current.CancellationToken); }
        if (state is "alias")
        {
            File.Move(source.Location, source.Location + ".move");
            File.Move(source.Location + ".move", Path.Combine(Path.GetDirectoryName(source.Location)!, "Example.instructions.md"));
        }
        try
        {
            await ((Func<Task>)(() => store.PrepareInstructionRegistration(source, TestContext.Current.CancellationToken)))
                .Should().ThrowAsync<InvalidDataException>();
            store.IsInstructionRegistered(source).Should().BeFalse();
        }
        finally { if (File.Exists(source.Location)) { File.SetAttributes(source.Location, FileAttributes.Normal); } }
    }

    [Fact]
    public async Task UserInstructionCreationAndUninspectedOriginalWritesAreNotGrantedByInventory()
    {
        var store = Create();
        var source = await Original(store);
        var bytes = await File.ReadAllBytesAsync(source.Location, TestContext.Current.CancellationToken);
        await ((Func<Task>)(() => store.Create(CustomizationKind.Instruction, CustomizationScope.User, "new-original", TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => Save(store, source, null, new CustomizationFile(source.PrimaryFile, bytes)))).Should().ThrowAsync<InvalidOperationException>();
        await Register(store, source);
        await ((Func<Task>)(() => Save(store, source, null, new CustomizationFile(source.PrimaryFile, bytes)))).Should().ThrowAsync<InvalidDataException>();
        (await File.ReadAllBytesAsync(source.Location, TestContext.Current.CancellationToken)).Should().Equal(bytes);
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("ancestor")]
    [InlineData("junction")]
    public async Task ChangedOrReparseParentsNeverRetargetTheRegisteredOriginal(string state)
    {
        var store = Create();
        var source = await Original(store);
        await Register(store, source);
        var before = await store.Read(source, TestContext.Current.CancellationToken);
        var directory = Path.GetDirectoryName(source.Location)!;
        if (state is "ancestor")
        {
            var user = Path.GetDirectoryName(directory)!;
            Directory.Move(user, user + ".retired");
            Directory.CreateDirectory(user);
            Directory.Move(Path.Combine(user + ".retired", "prompts"), directory);
        }
        else
        {
            Directory.Move(directory, directory + ".retired");
            if (state is "junction")
            {
                using var process = Process.Start(new ProcessStartInfo("cmd.exe")
                {
                    Arguments = "/c mklink /J \"" + directory + "\" \"" + directory + ".retired\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                })!;
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                process.ExitCode.Should().Be(0);
            }
            else
            {
                using var identity = WindowsIdentity.GetCurrent();
                RestrictedStorageDirectory.CreateRestrictedDirectory(directory,
                    identity.User ?? throw new InvalidOperationException("The private fixture owner is unavailable."));
                WindowsOwnedInstructionStoreTests.WriteOwnedFile(source.Location, before.Files.Single().CopyBytes());
            }
        }
        try
        {
            await ((Func<Task>)(() => store.Read(source, TestContext.Current.CancellationToken))).Should().ThrowAsync<Exception>();
            await ((Func<Task>)(() => Save(store, source, before, before.Files.Single().WithText(before.Files.Single().Text + "Draft"))))
                .Should().ThrowAsync<Exception>();
            (await File.ReadAllBytesAsync(source.Location, TestContext.Current.CancellationToken)).Should().Equal(before.Files.Single().CopyBytes());
        }
        finally { if (state is "junction") { Directory.Delete(directory); } }
    }

    [Theory]
    [InlineData(@"\\server\share\example.instructions.md")]
    [InlineData(@"C:\profile\..\example.instructions.md")]
    [InlineData(@"C:\Windows\example.instructions.md")]
    public async Task CallerPathsCannotReplaceRegisteredHostInventoryIdentity(string path)
    {
        var store = Create();
        var source = await Original(store);
        await ((Func<Task>)(() => store.PrepareInstructionRegistration(source with { Location = path }, TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<InvalidOperationException>();
        store.IsInstructionRegistered(source).Should().BeFalse();
    }

    [Fact]
    public async Task ConfiguredInstructionDirectoryDoesNotGrantProfileOriginalRegistrationOrWriteAuthority()
    {
        var directory = Path.Combine(root, "configured-instructions");
        Directory.CreateDirectory(root);
        using var identity = WindowsIdentity.GetCurrent();
        RestrictedStorageDirectory.CreateRestrictedDirectory(directory, identity.User!);
        var path = Path.Combine(directory, "example.instructions.md");
        var bytes = ArtifactFileDocument.Encode(CustomizationLayout.Template(CustomizationKind.Instruction, "example"), true);
        WindowsOwnedInstructionStoreTests.WriteOwnedFile(path, bytes);
        var preferences = new DirectoryPreferences(new([], [directory], [], [], [], [], false));
        var store = new WindowsCustomizationStore(new Paths(root), Path.Combine(root, "editor-profile"),
            Path.Combine(root, "user-profile"), preferences);
        var source = (await store.List(TestContext.Current.CancellationToken)).Should().ContainSingle().Subject;
        source.Scope.Should().Be(CustomizationScope.User);
        (await store.ListMetadataSources(TestContext.Current.CancellationToken)).Should().Equal(source);
        await ((Func<Task>)(() => store.PrepareInstructionRegistration(source, TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<InvalidDataException>();
        await ((Func<Task>)(() => store.Read(source, TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => Save(store, source, null, Primary(source, "Unapproved change"))))
            .Should().ThrowAsync<InvalidOperationException>();
        store.RegisteredInstruction.Should().BeNull();
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(bytes);
        Directory.Exists(Path.Combine(root, "local", "DefinitionTransactions")).Should().BeFalse();
        Directory.EnumerateFiles(directory).Should().Equal(path);
    }

    [Fact]
    public async Task AcceptedOptimisticSameNameReplacementWindowRemainsWholeBytePublicationWithoutFollowingOtherTargets()
    {
        var store = Create();
        var source = await Original(store);
        await Register(store, source);
        var before = await store.Read(source, TestContext.Current.CancellationToken);
        var proposed = before.Files.Single().WithText(before.Files.Single().Text + "\nReviewed proposal.");
        var external = before.Files.Single().WithText(before.Files.Single().Text + "\nLast external replacement.").CopyBytes();
        store.AfterInstructionFinalComparison = () =>
        {
            File.Move(source.Location, source.Location + ".old");
            WindowsOwnedInstructionStoreTests.WriteOwnedFile(source.Location, external);
        };
        var saved = await Save(store, source, before, proposed);
        saved.Files.Single().CopyBytes().Should().Equal(proposed.CopyBytes());
        (await File.ReadAllBytesAsync(source.Location + ".old", TestContext.Current.CancellationToken)).Should().Equal(before.Files.Single().CopyBytes());
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string fileName, string existingFileName, nint security);
}