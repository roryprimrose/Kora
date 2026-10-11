using System.Runtime.InteropServices;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Windows.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsOwnedInstructionStoreTests : IDisposable
{
    private readonly string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        ".net-test-artifacts", "instruction-tests", Guid.NewGuid().ToString("N")));

    private WindowsOwnedInstructionStore Create()
    {
        Directory.CreateDirectory(root);
        return new(new Paths(root));
    }

    private static ArtifactFileDocument Document(string body = "Original", bool bom = false) =>
        ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            $"---\r\nkora-format: 1\r\nname: example\r\ndescription: Description.\r\nattribution: Own text.\r\n---\r\n{body}\r\n",
            bom), ArtifactKind.Instruction, "kora-profile", "example");

    [Fact]
    public async Task SavePublishesExactBytesAndDeletionUsesTheReviewedIdentity()
    {
        var store = Create();
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

    [Fact]
    public async Task DetectedExternalReplacementConflictsAndExplicitReviewedOverwriteCanConflictAgain()
    {
        var store = Create();
        var created = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        File.WriteAllBytes(Path.Combine(root, "Instructions", "example.instructions.md"), Document("External").CopyBytes());
        var conflict = await store.Write("example", created.Current, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        conflict.Conflict.Should().BeTrue();
        conflict.Current!.Document.Definition.Content.Should().Be("External");
        File.WriteAllBytes(Path.Combine(root, "Instructions", "example.instructions.md"), Document("External again").CopyBytes());
        var renewed = await store.Write("example", conflict.Current, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        renewed.Conflict.Should().BeTrue();
        var overwritten = await store.Write("example", renewed.Current, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        overwritten.Published.Should().BeTrue();
        overwritten.Current!.Document.Definition.Content.Should().Be("Draft");
    }

    [Fact]
    public async Task ContentOnlyEditAfterFinalComparisonAcceptsWholeByteLastWriterWins()
    {
        var store = Create();
        var created = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        store.AfterFinalComparison = () =>
            File.WriteAllBytes(Path.Combine(root, "Instructions", "example.instructions.md"), Document("Narrow race").CopyBytes());
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
        File.WriteAllBytes(temporary, Document("External atomic revision").CopyBytes());
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
    public async Task DirectoryNamedAsAnInstructionCannotBeReplacedOrFollowed()
    {
        var store = Create();
        await store.List(TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Path.Combine(root, "Instructions", "example.instructions.md"));
        var write = () => store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        await write.Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(Path.Combine(root, "Instructions", "example.instructions.md")).Should().BeTrue();
    }

    [Fact]
    public async Task ReplacedParentFailsClosedAndNoDraftIsWrittenElsewhere()
    {
        var store = Create();
        await store.List(TestContext.Current.CancellationToken);
        Directory.Move(Path.Combine(root, "Instructions"), Path.Combine(root, "old"));
        Directory.CreateDirectory(Path.Combine(root, "Instructions"));
        var write = () => store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        await write.Should().ThrowAsync<InvalidDataException>();
        Directory.GetFiles(Path.Combine(root, "Instructions")).Should().BeEmpty();
    }

    [Fact]
    public async Task HardlinkedTargetIsRejectedWithoutModifyingEitherAlias()
    {
        var store = Create();
        await store.List(TestContext.Current.CancellationToken);
        var target = Path.Combine(root, "Instructions", "example.instructions.md");
        var other = Path.Combine(root, "outside.instructions.md");
        File.WriteAllBytes(other, Document("Outside").CopyBytes());
        CreateHardLink(target, other, nint.Zero).Should().BeTrue();
        var read = () => store.Read("example", TestContext.Current.CancellationToken);
        await read.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(other).Should().Equal(Document("Outside").CopyBytes());
    }

    [Fact]
    public async Task ReparseOwnedDirectoryIsRejectedWithoutFollowingItsPrivateTarget()
    {
        var store = Create();
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        var junction = Path.Combine(root, "Instructions");
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

    [Fact]
    public async Task PinnedDirectoryCannotBeRedirectedAtTheFinalPublicationBoundary()
    {
        var store = Create();
        var created = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        var blocked = false;
        store.AfterFinalComparison = () =>
        {
            try { Directory.Move(Path.Combine(root, "Instructions"), Path.Combine(root, "redirected")); }
            catch (IOException) { blocked = true; }
        };
        var result = await store.Write("example", created.Current, Document("Draft"), () => true, TestContext.Current.CancellationToken);
        blocked.Should().BeTrue();
        result.Published.Should().BeTrue();
        Directory.Exists(Path.Combine(root, "redirected")).Should().BeFalse();
    }

    [Fact]
    public async Task CancellationBeforePublicationPreservesOriginalAndCleansPrivateStaging()
    {
        var store = Create();
        var created = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        store.AfterFinalComparison = () => cancellation.Cancel();
        var write = () => store.Write("example", created.Current, Document("Draft"), () => true, cancellation.Token);
        await write.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllBytes(Path.Combine(root, "Instructions", "example.instructions.md")).Should().Equal(Document().CopyBytes());
        Directory.GetFiles(Path.Combine(root, "Instructions"), "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationAfterPublicationReportsObservedEffectAndNeverClaimsRollback()
    {
        var store = Create();
        var created = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        store.AfterPublication = () => cancellation.Cancel();
        var write = () => store.Write("example", created.Current, Document("Published"), () => true, cancellation.Token);
        var failure = await write.Should().ThrowAsync<InstructionPublicationUnconfirmedException>();
        failure.Which.CancellationRequested.Should().BeTrue();
        File.ReadAllBytes(Path.Combine(root, "Instructions", "example.instructions.md")).Should().Equal(Document("Published").CopyBytes());
        Directory.GetFiles(Path.Combine(root, "Instructions"), "*.tmp").Should().BeEmpty();
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

    private sealed record Paths(string RoamingRoot) : IApplicationDataPaths
    {
        public string LocalRoot => Path.Combine(RoamingRoot, "Local");
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string newName, string existingName, nint security);
}
