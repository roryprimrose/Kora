using System.Text;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Kora.Core.Skills;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;

namespace Kora.Windows.IntegrationTests.Skills;

public sealed partial class OwnedSkillMarkdownPublicationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static OwnedDeclarativeSkillDocument Document(string version = "1.0.0") =>
        OwnedDeclarativeSkillDocument.CreatePackage("example", version, "Example", "Description", @"Read [guide](references\guide.md).",
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [@"references\guide.md"] = "Exact reference\r\n"u8.ToArray(),
                [@"references\more\notes.md"] = "More notes."u8.ToArray(),
                ["rubric.md"] = "Plain rubric."u8.ToArray(),
            });
    private static Task<OwnedDeclarativeSkillSnapshot> Save(WindowsOwnedDeclarativeSkillStore store,
        OwnedDeclarativeSkillDocument document, OwnedDeclarativeSkillSnapshot? source = null) =>
        store.Publish(document, static () => true, operation => Task.FromResult(operation()), Token, source);

    [Fact]
    public async Task WholeTreeDurableReadbackRestartAndSourcePinnedNewVersionRemainDisabled()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = Document();
        var saved = await Save(store, document);
        saved.Document.RevisionDigest.Should().Be(document.RevisionDigest);
        saved.ReferenceIdentities.Should().HaveCount(3);
        saved.TotalBytes.Should().Be(document.TotalBytes);
        var restarted = new WindowsOwnedDeclarativeSkillStore(paths);
        var declaration = (await restarted.Read("example", "1.0.0", Token, OwnedSkillReadScope.PrimaryDeclaration))!;
        declaration.Document.IsComplete.Should().BeFalse();
        declaration.Document.References.Should().BeEmpty();
        declaration.TotalBytes.Should().Be(document.TotalBytes);
        (await restarted.List(Token)).Single().Document.IsComplete.Should().BeFalse();
        var reopened = (await restarted.Read("example", "1.0.0", Token))!;
        saved.IsSameRevision(reopened).Should().BeTrue();
        reopened.Document.IsAvailableForInvocation.Should().BeFalse();
        await Save(restarted, Document("1.0.1"), reopened);
        (await restarted.List(Token)).Should().HaveCount(2);
        await restarted.Invoking(value => Save(value, Document("1.0.1"))).Should().ThrowAsync<InvalidOperationException>();
        await File.WriteAllTextAsync(Path.Combine(store.Destination(document), @"references\guide.md"), "Changed", Token);
        await restarted.Invoking(value => Save(value, Document("1.0.2"), reopened)).Should().ThrowAsync<InvalidDataException>();
        (await restarted.Read("example", "1.0.2", Token)).Should().BeNull();
    }

    [Theory]
    [InlineData("reference-flushed")]
    [InlineData("before-commit")]
    [InlineData("after-commit")]
    public async Task ComponentFaultsCannotPublishPartialOrSilentlyUsableTrees(string fault)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths)
        {
            Checkpoint = checkpoint => { if (string.Equals(checkpoint, fault, StringComparison.Ordinal)) { throw new IOException("injected component failure"); } },
        };
        var publish = () => Save(store, Document());
        if (fault is "after-commit") { await publish.Should().ThrowAsync<OwnedSkillPublicationUnconfirmedException>(); }
        else { await publish.Should().ThrowAsync<IOException>(); }
        store.Checkpoint = null;
        var saved = await store.Read("example", "1.0.0", Token);
        if (fault is "after-commit")
        {
            saved!.Document.RevisionDigest.Should().Be(Document().RevisionDigest);
            saved.Document.IsAvailableForInvocation.Should().BeFalse();
        }
        else { saved.Should().BeNull(); }
        Directory.EnumerateDirectories(Path.Combine(paths.RoamingRoot, "Skills", "example"), ".owned-skill-*").Should().BeEmpty();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("alias")]
    [InlineData("binary")]
    [InlineData("oversized")]
    public async Task ExactDeclaredInventoryAndByteLimitsNeverDropUnsafeComponents(string fault)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = Document();
        await Save(store, document);
        var root = store.Destination(document);
        var reference = Path.Combine(root, @"references\guide.md");
        if (fault is "unknown") { await File.WriteAllTextAsync(Path.Combine(root, "extra.md"), "Unknown", Token); }
        if (fault is "missing") { File.Delete(reference); }
        if (fault is "alias") { File.Move(reference, Path.Combine(root, @"references\Guide.md")); }
        if (fault is "binary") { await File.WriteAllBytesAsync(reference, [0xff], Token); }
        if (fault is "oversized") { await File.WriteAllTextAsync(reference, new string('x', 16385), Encoding.UTF8, Token); }
        await store.Invoking(value => value.Read("example", "1.0.0", Token)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task DeclarationInspectionNeverOpensComponentBodies()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = Document();
        await Save(store, document);
        using var blocked = new FileStream(Path.Combine(store.Destination(document), @"references\guide.md"),
            FileMode.Open, FileAccess.Read, FileShare.None);
        var declaration = await store.Read("example", "1.0.0", Token, OwnedSkillReadScope.PrimaryDeclaration);
        declaration!.Document.IsComplete.Should().BeFalse();
        await store.Invoking(value => value.Read("example", "1.0.0", Token)).Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task ChangedPrimaryCannotSmuggleDifferentReferenceReadsUnderOldConsent()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = Document();
        await Save(store, document);
        var expected = (await store.Read("example", "1.0.0", Token, OwnedSkillReadScope.PrimaryDeclaration))!;
        await File.WriteAllBytesAsync(Path.Combine(store.Destination(document), "manifest.yaml"),
            Encoding.UTF8.GetBytes(document.ManifestText.Replace(@"references\guide.md", "unreviewed.md", StringComparison.Ordinal)), Token);
        var reads = 0;
        store.Checkpoint = point => { if (point is "before-reference-read") { reads++; } };
        await store.Invoking(value => value.Read("example", "1.0.0", Token, expectedPrimary: expected))
            .Should().ThrowAsync<InvalidDataException>();
        reads.Should().Be(0);
    }

    [Fact]
    public async Task SourceFilesRemainPinnedAgainstWritesAndReplacementUntilNewWholeVersionIsPublished()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var source = await Save(store, Document());
        var original = Path.Combine(store.Destination(source.Document), @"references\guide.md");
        var attempts = 0;
        store.Checkpoint = point =>
        {
            if (point is not "before-commit") { return; }
            ((Action)(() => File.WriteAllText(original, "Changed source"))).Should().Throw<IOException>();
            ((Action)(() => File.Move(original, original + ".moved"))).Should().Throw<IOException>();
            attempts++;
        };
        var published = await Save(store, Document("1.0.1"), source);
        attempts.Should().Be(1);
        published.Document.IsAvailableForInvocation.Should().BeFalse();
        source.IsSameRevision((await store.Read("example", "1.0.0", Token))!).Should().BeTrue();
    }

    [Fact]
    public async Task ADeclaredReferenceHardlinkCannotBecomeAReviewedPackageComponent()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = Document();
        await Save(store, document);
        var reference = Path.Combine(store.Destination(document), @"references\guide.md");
        CreateHardLink(Path.Combine(paths.RoamingRoot, "outside-alias.md"), reference, nint.Zero).Should().BeTrue();
        await store.Invoking(value => value.Read("example", "1.0.0", Token)).Should().ThrowAsync<InvalidDataException>();
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string link, string existing, nint security);
}
