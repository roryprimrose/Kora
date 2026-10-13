using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Windows.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsOwnedInstructionStoreTests : IDisposable
{
    private ArtifactKind kind = ArtifactKind.Instruction;
    private string OwnedDirectory => Path.Combine(root, OwnedDefinitionKind.DirectoryName(kind));
    private string Target => Path.Combine(OwnedDirectory, "example" + OwnedDefinitionKind.Extension(kind));
    private readonly string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        ".net-test-artifacts", "instruction-tests", Guid.NewGuid().ToString("N")));

    private WindowsOwnedInstructionStore Create(ArtifactKind kind = ArtifactKind.Instruction)
    {
        this.kind = kind;
        Directory.CreateDirectory(root);
        return new(new Paths(root), kind);
    }

    private ArtifactFileDocument Document(string body = "Original", bool bom = false) =>
        ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            $"---\r\nkora-format: 1\r\nname: example\r\ndescription: Description.\r\nattribution: Own text.\r\n---\r\n{body}\r\n",
            bom), kind, "kora-profile", "example");

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task SavePublishesExactBytesAndDeletionUsesTheReviewedIdentity(ArtifactKind kind)
    {
        var store = Create(kind);
        var document = Document(bom: true);
        var created = await store.Write("example", null, document, () => true, TestContext.Current.CancellationToken);
        created.Published.Should().BeTrue();
        created.Current!.Document.CopyBytes().Should().Equal(document.CopyBytes());
        (await store.List(TestContext.Current.CancellationToken)).Should().Equal("example");
        var unchanged = await store.Write("example", created.Current, document, () => true, TestContext.Current.CancellationToken);
        unchanged.Published.Should().BeFalse();
        unchanged.Unchanged.Should().BeTrue();
        var deleted = await store.Write("example", unchanged.Current, null, () => true, TestContext.Current.CancellationToken);
        deleted.Deleted.Should().BeTrue();
        (await store.Read("example", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task DetectedExternalReplacementConflictsAndExplicitReviewedOverwriteCanConflictAgain(ArtifactKind kind)
    {
        var store = Create(kind);
        var created = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        File.WriteAllBytes(Target, Document("External").CopyBytes());
        var conflict = await store.Write("example", created.Current, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        conflict.Conflict.Should().BeTrue();
        conflict.Current!.Document.Definition.Content.Should().Be("External");
        File.WriteAllBytes(Target, Document("External again").CopyBytes());
        var renewed = await store.Write("example", conflict.Current, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        renewed.Conflict.Should().BeTrue();
        var overwritten = await store.Write("example", renewed.Current, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        overwritten.Published.Should().BeTrue();
        overwritten.Current!.Document.Definition.Content.Should().Be("Draft");
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task ContentOnlyEditAfterFinalComparisonAcceptsWholeByteLastWriterWins(ArtifactKind kind)
    {
        var store = Create(kind);
        var created = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        store.AfterFinalComparison = () =>
            File.WriteAllBytes(Target, Document("Narrow race").CopyBytes());
        var saved = await store.Write("example", created.Current, Document("Kora draft", bom: true), () => true,
            TestContext.Current.CancellationToken);
        saved.Published.Should().BeTrue();
        saved.Current!.Document.CopyBytes().Should().Equal(Document("Kora draft", bom: true).CopyBytes());
    }

    [Fact]
    public async Task AtomicExternalReplacementBeforeSaveIsADetectedRevisionConflict()
    {
        var store = Create();
        var created = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        var temporary = Path.Combine(root, "Instructions", ".external.tmp");
        WriteOwnedFile(temporary, Document("External atomic revision").CopyBytes());
        File.Move(temporary, Path.Combine(root, "Instructions", "example.instructions.md"), overwrite: true);
        var result = await store.Write("example", created.Current, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        result.Conflict.Should().BeTrue();
        result.Current!.FileIdentity.Should().NotBe(created.Current!.FileIdentity);
        result.Current.Document.Definition.Content.Should().Be("External atomic revision");
        Directory.GetFiles(Path.Combine(root, "Instructions"), "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task MissingOriginalConflictsAndAnExplicitNewReviewedBaseCanRecreateIt()
    {
        var store = Create();
        var created = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        File.Delete(Path.Combine(root, "Instructions", "example.instructions.md"));
        var conflict = await store.Write("example", created.Current, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        conflict.Conflict.Should().BeTrue();
        conflict.Current.Should().BeNull();
        var recreated = await store.Write("example", null, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        recreated.Published.Should().BeTrue();
    }

    [Fact]
    public async Task FirstOwnedApplicationDirectoryCanBeCreatedButBoundSourceLossCannotBeRecreatedImplicitly()
    {
        Directory.CreateDirectory(root);
        var roaming = Path.Combine(root, "fresh-roaming");
        var store = new WindowsOwnedInstructionStore(new Paths(roaming));
        (await store.List(TestContext.Current.CancellationToken)).Should().BeEmpty();
        Directory.Exists(Path.Combine(roaming, "Instructions")).Should().BeTrue();
        Directory.Delete(Path.Combine(roaming, "Instructions"));
        var read = () => store.Read("example", TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(Path.Combine(roaming, "Instructions")).Should().BeFalse();
    }

    [Fact]
    public async Task LegacyFilenameAndCaseAliasesRemainInspectableWithoutImplicitRenameAuthority()
    {
        var store = Create();
        await store.List(TestContext.Current.CancellationToken);
        var path = Path.Combine(root, "Instructions", "Legacy.Name.instructions.md");
        WriteOwnedFile(path, Document().CopyBytes());
        (await store.List(TestContext.Current.CancellationToken)).Should().Equal("Legacy.Name");
        var original = await store.Read("legacy.name", TestContext.Current.CancellationToken);
        original!.Name.Should().Be("Legacy.Name");
        original.CanonicalFileName.Should().Be("Legacy.Name.instructions.md");
        var write = () => store.Write("Legacy.Name", original, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        await write.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(path).Should().Equal(Document().CopyBytes());
    }

    [Fact]
    public async Task CanonicalBaseWithNoncanonicalSuffixCannotBeImplicitlyRenamedBySave()
    {
        var store = Create();
        await store.List(TestContext.Current.CancellationToken);
        var path = Path.Combine(root, "Instructions", "example.instructions.MD");
        WriteOwnedFile(path, Document().CopyBytes());
        var original = await store.Read("example", TestContext.Current.CancellationToken);
        original!.CanonicalFileName.Should().Be("example.instructions.MD");
        var write = () => store.Write("example", original, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        await write.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(path).Should().Equal(Document().CopyBytes());
    }

    [Fact]
    public async Task DirectoryNamedAsAnInstructionCannotBeReplacedOrFollowed()
    {
        var store = Create();
        await store.List(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(root, "Instructions", "example.instructions.md"));
        var write = () => store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        await write.Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(Path.Combine(root, "Instructions", "example.instructions.md")).Should().BeTrue();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task ReplacedParentFailsClosedAndNoDraftIsWrittenElsewhere(ArtifactKind kind)
    {
        var store = Create(kind);
        await store.List(TestContext.Current.CancellationToken);
        Directory.Move(OwnedDirectory, Path.Combine(root, "old"));
        using var identity = WindowsIdentity.GetCurrent();
        Kora.Windows.Storage.RestrictedStorageDirectory.CreateRestrictedDirectory(OwnedDirectory, identity.User!);
        var write = () => store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        await write.Should().ThrowAsync<InvalidDataException>();
        Directory.GetFiles(OwnedDirectory).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task HardlinkedTargetIsRejectedWithoutModifyingEitherAlias(ArtifactKind kind)
    {
        var store = Create(kind);
        await store.List(TestContext.Current.CancellationToken);
        var target = Target;
        var other = Path.Combine(root, "outside.instructions.md");
        File.WriteAllBytes(other, Document("Outside").CopyBytes());
        CreateHardLink(target, other, nint.Zero).Should().BeTrue();
        var read = () => store.Read("example", TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(other).Should().Equal(Document("Outside").CopyBytes());
    }

    [Fact]
    public async Task KindMismatchAndUnsupportedKindsCannotPublishIntoAnotherOwnedStore()
    {
        var instructions = Create();
        var instruction = Document();
        var original = await instructions.Write("example", null, instruction, () => true, TestContext.Current.CancellationToken);
        var prompts = new WindowsOwnedInstructionStore(new Paths(root), ArtifactKind.Prompt);
        var replacement = () => prompts.Write("example", null, instruction, () => true, TestContext.Current.CancellationToken);
        await replacement.Should().ThrowAsync<InvalidDataException>();
        var wrongBase = () => prompts.Write("example", original.Current, null, () => true, TestContext.Current.CancellationToken);
        await wrongBase.Should().ThrowAsync<InvalidDataException>();
        ((Action)(() => _ = new WindowsOwnedInstructionStore(new Paths(root), ArtifactKind.Agent))).Should().Throw<InvalidDataException>();
        File.ReadAllBytes(Target).Should().Equal(instruction.CopyBytes());
        Directory.Exists(Path.Combine(root, "Prompts")).Should().BeFalse();
    }

    [Theory]
    [InlineData("Example.prompt.md")]
    [InlineData("example.prompt.MD")]
    [InlineData("example.prompt.md", "kora-format: 2")]
    [InlineData("example.prompt.md", "parameters: required")]
    public async Task ManagedPromptAliasesAndUnsupportedFormatsCannotBecomeUsableRevisions(string filename, string? metadata = null)
    {
        var store = Create(ArtifactKind.Prompt);
        await store.List(TestContext.Current.CancellationToken);
        var path = Path.Combine(OwnedDirectory, filename);
        var bytes = metadata is null ? Document().CopyBytes()
            : ArtifactFileDocument.Encode(Document().Text.Replace("kora-format: 1", "kora-format: 1\n" + metadata, StringComparison.Ordinal), false);
        WriteOwnedFile(path, bytes);
        var read = () => store.Read("example", TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(path).Should().Equal(bytes);
    }
    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task ReparseOwnedDirectoryIsRejectedWithoutFollowingItsPrivateTarget(ArtifactKind kind)
    {
        var store = Create(kind);
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        var junction = OwnedDirectory;
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c mklink /J \"" + junction + "\" \"" + outside + "\"",
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        })!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.Should().Be(0);
        try
        {
            var write = () => store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
            await write.Should().ThrowAsync<InvalidDataException>();
            Directory.GetFiles(outside).Should().BeEmpty();
        }
        finally { Directory.Delete(junction); }
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task PinnedDirectoryCannotBeRedirectedAtTheFinalPublicationBoundary(ArtifactKind kind)
    {
        var store = Create(kind);
        var created = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        var blocked = false;
        store.AfterFinalComparison = () =>
        {
            try { Directory.Move(OwnedDirectory, Path.Combine(root, "redirected")); }
            catch (IOException) { blocked = true; }
        };
        var result = await store.Write("example", created.Current, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        blocked.Should().BeTrue();
        result.Published.Should().BeTrue();
        Directory.Exists(Path.Combine(root, "redirected")).Should().BeFalse();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task CancellationBeforePublicationPreservesOriginalAndCleansPrivateStaging(ArtifactKind kind)
    {
        var store = Create(kind);
        var created = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        store.AfterFinalComparison = () => cancellation.Cancel();
        var write = () => store.Write("example", created.Current, Document("Draft"), () => true, cancellation.Token);
        await write.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllBytes(Target).Should().Equal(Document().CopyBytes());
        Directory.GetFiles(OwnedDirectory, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task CancellationAfterPublicationReportsObservedEffectAndNeverClaimsRollback(ArtifactKind kind)
    {
        var store = Create(kind);
        var created = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        store.AfterPublication = () => cancellation.Cancel();
        var write = () => store.Write("example", created.Current, Document("Published"), () => true, cancellation.Token);
        var failure = await write.Should().ThrowAsync<InstructionPublicationUnconfirmedException>();
        failure.Which.CancellationRequested.Should().BeTrue();
        File.ReadAllBytes(Target).Should().Equal(Document("Published").CopyBytes());
        Directory.GetFiles(OwnedDirectory, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("../bad")]
    [InlineData("con")]
    [InlineData("COM1")]
    [InlineData("bad.name")]
    [InlineData("bad\\file")]
    public async Task InvalidTargetsNeverBecomeWritePaths(string name)
    {
        var store = Create();
        var write = () => store.Write(name, null, Document(), () => true, TestContext.Current.CancellationToken);
        await write.Should().ThrowAsync<InvalidDataException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }

    internal static void WriteOwnedFile(string path, byte[] bytes)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidOperationException("The private fixture owner is unavailable.");
        var security = new FileSecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
        using var stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Read | FileSystemRights.Write,
            FileShare.None, 4096, FileOptions.None, security);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private sealed record Paths(string RoamingRoot) : IApplicationDataPaths
    {
        public string LocalRoot => Path.Combine(RoamingRoot, "Local");
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string newName, string existingName, nint security);
}
