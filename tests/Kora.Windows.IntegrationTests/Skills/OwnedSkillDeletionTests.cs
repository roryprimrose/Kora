using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using Kora.Core.Skills;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Skills;

public sealed partial class OwnedSkillDeletionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static OwnedDeclarativeSkillDocument Document(string version = "1.0.0") =>
        OwnedDeclarativeSkillDocument.Create("example", version, "Example", "Description", "complete literal guidance");
    private static Task<OwnedDeclarativeSkillSnapshot> Save(WindowsOwnedDeclarativeSkillStore store, OwnedDeclarativeSkillDocument document) =>
        store.Publish(document, static () => true, operation => Task.FromResult(operation()), Token);
    private static Task Delete(WindowsOwnedDeclarativeSkillStore store, OwnedSkillDeletionSnapshot source, Func<bool>? eligible = null) =>
        store.Delete(source, eligible ?? (static () => true), operation => Task.FromResult(operation()), Token);

    [Theory]
    [InlineData(OwnedSkillGuidanceFormat.Literal, OwnedSkillToolSelection.None)]
    [InlineData(OwnedSkillGuidanceFormat.Literal, OwnedSkillToolSelection.WebPageGet)]
    [InlineData(OwnedSkillGuidanceFormat.RequiredLiteralInputs, OwnedSkillToolSelection.None)]
    public async Task ExactFiniteNativeInventoryIsDeletedAndUnrelatedSourcesAndRecordsSurvive(
        OwnedSkillGuidanceFormat format, OwnedSkillToolSelection tool)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description",
            format == OwnedSkillGuidanceFormat.RequiredLiteralInputs ? "Review ${input:topic}" : "Complete guidance", tool, format);
        var selected = await Save(store, document);
        var other = await Save(store, Document("1.0.1"));
        var independent = Path.Combine(paths.LocalRoot, "independent-grants-history-audit");
        await File.WriteAllTextAsync(independent, "independent records", Token);
        var review = await store.PrepareDeletion("example", "1.0.0", Token);
        review.Source.IsSameRevision(selected).Should().BeTrue();
        await store.ValidateDeletion(review, Token);
        await Delete(store, review);
        Directory.Exists(store.Destination(document)).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(store.Destination(document))).Should().BeTrue();
        (await store.Read("example", "1.0.0", Token)).Should().BeNull();
        (await store.List(Token)).Should().ContainSingle().Which.IsSameRevision(other).Should().BeTrue();
        (await File.ReadAllTextAsync(independent, Token)).Should().Be("independent records");
        await store.Invoking(value => value.PrepareDeletion("example", "1.0.0", Token)).Should().ThrowAsync<InvalidOperationException>();
        await store.Invoking(value => Save(value, document)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("validate")]
    [InlineData("delete")]
    public async Task MarkdownPackageDeletionIsRejectedBeforeReferenceReadsOrEffects(string operation)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = OwnedDeclarativeSkillDocument.CreatePackage("example", "1.0.0", "Example", "Description",
            "Complete literal guidance", new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["notes.md"] = "Private reference body."u8.ToArray(),
            });
        var saved = await Save(store, document);
        var other = await Save(store, Document("1.0.1"));
        var admitted = await store.PrepareDeletion("example", "1.0.1", Token);
        var forged = admitted with { Source = saved, Destination = store.Destination(document) };
        var referenceReads = 0;
        var deletionEffects = 0;
        store.Checkpoint = point =>
        {
            if (point is "before-reference-read") { referenceReads++; }
            if (point is "before-delete") { deletionEffects++; }
        };
        Func<Task> attempt = operation switch
        {
            "prepare" => () => store.PrepareDeletion("example", "1.0.0", Token),
            "validate" => () => store.ValidateDeletion(forged, Token),
            _ => () => Delete(store, forged),
        };
        await attempt.Should().ThrowAsync<InvalidDataException>();
        referenceReads.Should().Be(0);
        deletionEffects.Should().Be(0);
        (await store.List(Token)).Should().HaveCount(2);
        referenceReads.Should().Be(0);
        store.Checkpoint = null;
        saved.IsSameRevision((await store.Read("example", "1.0.0", Token))!).Should().BeTrue();
        other.IsSameRevision((await store.Read("example", "1.0.1", Token))!).Should().BeTrue();
        saved.Document.IsAvailableForInvocation.Should().BeFalse();
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("file")]
    [InlineData("parent")]
    [InlineData("source")]
    [InlineData("root")]
    [InlineData("catalogue")]
    [InlineData("unknown")]
    [InlineData("permission")]
    [InlineData("destination")]
    public async Task ChangedNativeSourceDestinationOrInventoryNeverDeletesAnyEntry(string change)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = Document();
        await Save(store, document);
        var review = await store.PrepareDeletion("example", "1.0.0", Token);
        var target = store.Destination(document);
        var file = Path.Combine(target, "instructions.md");
        var originalTarget = target;
        var directory = RestrictedStorageDirectory.ForOwnedSkills(paths);
        if (change is "bytes") { await File.WriteAllTextAsync(file, "changed", Token); }
        if (change is "file")
        {
            File.Move(file, file + ".original");
            using (var stream = directory.CreateNewFile(file)) { stream.Write(document.InstructionBytes.Span); }
            File.Delete(file + ".original");
        }
        if (change is "source" or "parent" or "root")
        {
            var path = change is "source" ? target : change is "parent" ? Path.GetDirectoryName(target)! : Path.GetDirectoryName(Path.GetDirectoryName(target))!;
            Directory.Move(path, path + "-original");
            originalTarget = path + "-original" + target[path.Length..];
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User ?? throw new InvalidOperationException("The fixture needs its native owner.");
            RestrictedStorageDirectory.CreateRestrictedDirectory(path, user);
            if (change is "root") { directory.CreateOwnedSkillDirectory(Path.GetDirectoryName(target)!); }
            if (change is not "source") { directory.CreateOwnedSkillDirectory(target); }
            using (var stream = directory.CreateNewFile(Path.Combine(target, "manifest.yaml"))) { stream.Write(document.ManifestBytes.Span); }
            using (var stream = directory.CreateNewFile(file)) { stream.Write(document.InstructionBytes.Span); }
        }
        if (change is "catalogue") { await Save(store, Document("1.0.1")); }
        if (change is "unknown") { await File.WriteAllTextAsync(Path.Combine(target, "external.md"), "external", Token); }
        if (change is "permission")
        {
            var info = new FileInfo(file);
            var acl = info.GetAccessControl();
            acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.ReadData, AccessControlType.Allow));
            info.SetAccessControl(acl);
        }
        if (change is "destination") { review = review with { Destination = paths.LocalRoot }; }
        var operation = () => Delete(store, review);
        if (change is "permission") { await operation.Should().ThrowAsync<UnauthorizedAccessException>(); }
        else { await operation.Should().ThrowAsync<InvalidDataException>(); }
        Directory.Exists(target).Should().BeTrue();
        File.Exists(file).Should().BeTrue();
        File.Exists(Path.Combine(originalTarget, "manifest.yaml")).Should().BeTrue();
    }

    [Theory]
    [InlineData("before-delete")]
    [InlineData("after-delete-file")]
    [InlineData("after-delete")]
    public async Task FaultsAreTruthfullyUnconfirmedAfterFirstEffectAndNeverRetried(string point)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        await Save(store, Document());
        var review = await store.PrepareDeletion("example", "1.0.0", Token);
        store.Checkpoint = checkpoint => { if (string.Equals(checkpoint, point, StringComparison.Ordinal)) { throw new IOException("injected fixture fault"); } };
        var operation = () => Delete(store, review);
        if (point is "before-delete") { await operation.Should().ThrowAsync<IOException>(); }
        else { await operation.Should().ThrowAsync<OwnedSkillDeletionUnconfirmedException>(); }
        store.Checkpoint = null;
        if (point is "before-delete")
        { (await store.Read("example", "1.0.0", Token))!.IsSameRevision(review.Source).Should().BeTrue(); }
        else
        { await store.Invoking(value => Delete(value, review)).Should().ThrowAsync<InvalidDataException>(); }
        if (point is "after-delete-file")
        {
            File.Exists(Path.Combine(review.Destination, "manifest.yaml")).Should().BeFalse();
            File.Exists(Path.Combine(review.Destination, "instructions.md")).Should().BeTrue();
        }
    }

    [Fact]
    public async Task FinalNativePinsDenyWritesRenamesAndRootReplacementWhileRevocationPreventsEffect()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        await Save(store, Document());
        var review = await store.PrepareDeletion("example", "1.0.0", Token);
        var eligible = true;
        store.Checkpoint = point =>
        {
            if (point is not "before-delete") { return; }
            var write = () => File.WriteAllText(Path.Combine(review.Destination, "instructions.md"), "unauthorized");
            write.Should().Throw<IOException>();
            var rename = () => File.Move(Path.Combine(review.Destination, "manifest.yaml"), Path.Combine(review.Destination, "external"));
            rename.Should().Throw<IOException>();
            var replace = () => Directory.Move(Path.GetDirectoryName(review.Destination)!, review.Destination + "-parent");
            replace.Should().Throw<IOException>();
            eligible = false;
        };
        await store.Invoking(value => Delete(value, review, () => eligible)).Should().ThrowAsync<InvalidOperationException>();
        store.Checkpoint = null;
        (await store.Read("example", "1.0.0", Token))!.IsSameRevision(review.Source).Should().BeTrue();
    }

    [Fact]
    public async Task CancellationKeepsOwnershipUntilNativeWorkActuallyJoins()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        await Save(store, Document());
        var review = await store.PrepareDeletion("example", "1.0.0", Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var cancel = new CancellationTokenSource();
        store.Checkpoint = point => { if (point is "before-delete") { entered.SetResult(); release.Wait(Token); } };
        var operation = store.Delete(review, static () => true, commit => Task.FromResult(commit()), cancel.Token);
        await entered.Task.WaitAsync(Token);
        cancel.Cancel();
        operation.IsCompleted.Should().BeFalse();
        release.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        store.Checkpoint = null;
        (await store.Read("example", "1.0.0", Token))!.IsSameRevision(review.Source).Should().BeTrue();
    }

    [Fact]
    public async Task AbsentOrArbitrarySourcesNeverCreateAStoreOrClaimDeletion()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        await store.Invoking(value => value.PrepareDeletion("example", "1.0.0", Token)).Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(paths.RoamingRoot).Should().BeFalse();
        await store.Invoking(value => value.PrepareDeletion("..\\outside", "1.0.0", Token)).Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(paths.RoamingRoot).Should().BeFalse();
    }

    [Fact]
    public async Task HardLinkedSelectedFilesAndUnsupportedObservationPackagesRemainUntouched()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = Document();
        await Save(store, document);
        var source = await store.PrepareDeletion("example", "1.0.0", Token);
        var instructions = Path.Combine(source.Destination, "instructions.md");
        var alias = Path.Combine(paths.LocalRoot, "independent-alias.md");
        CreateHardLink(alias, instructions, nint.Zero).Should().BeTrue();
        await store.Invoking(value => Delete(value, source)).Should().ThrowAsync<InvalidDataException>();
        File.Exists(instructions).Should().BeTrue();
        (await File.ReadAllTextAsync(alias, Token)).Should().Be(document.Guidance);
        File.Delete(alias);
        var workflow = OwnedDeclarativeSkillDocument.CreateObservationWorkflow("inspection", "1.0.0", "Inspection", "Description",
            """{"steps":[{"tool":"application.get_version","input":{}}]}""");
        await Save(store, workflow);
        await store.Invoking(value => value.PrepareDeletion("inspection", "1.0.0", Token)).Should().ThrowAsync<InvalidDataException>();
        (await store.Read("inspection", "1.0.0", Token))!.Document.RevisionDigest.Should().Be(workflow.RevisionDigest);
    }

    [Fact]
    public async Task JunctionReplacementIsNotFollowedAndOnlyTheReviewedFiniteFilesCanEverBeRetired()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var document = Document();
        await Save(store, document);
        var source = await store.PrepareDeletion("example", "1.0.0", Token);
        var original = source.Destination + "-original";
        Directory.Move(source.Destination, original);
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            Arguments = $"/d /c mklink /J \"{source.Destination}\" \"{original}\"",
        }) ?? throw new InvalidOperationException("The fixture junction helper could not start.");
        await process.WaitForExitAsync(Token);
        process.ExitCode.Should().Be(0);
        try
        {
            await store.Invoking(value => Delete(value, source)).Should().ThrowAsync<InvalidDataException>();
            (await File.ReadAllTextAsync(Path.Combine(original, "instructions.md"), Token)).Should().Be(document.Guidance);
        }
        finally { Directory.Delete(source.Destination); }
    }

    [Fact]
    public async Task UnexpectedEntryAfterTheFirstEffectIsRetainedWithAnUnconfirmedOutcome()
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        await Save(store, Document());
        var source = await store.PrepareDeletion("example", "1.0.0", Token);
        var external = Path.Combine(source.Destination, "external.md");
        store.Checkpoint = point => { if (point is "after-delete-file") { File.WriteAllText(external, "external data must survive"); } };
        await store.Invoking(value => Delete(value, source)).Should().ThrowAsync<OwnedSkillDeletionUnconfirmedException>();
        (await File.ReadAllTextAsync(external, Token)).Should().Be("external data must survive");
        Directory.Exists(source.Destination).Should().BeTrue();
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string alias, string original, nint security);
}
