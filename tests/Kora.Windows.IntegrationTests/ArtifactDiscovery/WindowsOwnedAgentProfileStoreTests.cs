using System.Security.Principal;
using System.Runtime.InteropServices;

using AwesomeAssertions;

using Kora.Core.Agents;
using Kora.Core.Dependencies;
using Kora.Core.Artifacts;
using Kora.Application.Agents;
using Kora.Windows.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsOwnedAgentProfileStoreTests : IDisposable
{
    internal const string Source = """
        ---
        name: example
        description: Inert original
        kora-agent-format: 1
        runtime: local
        input: text
        output: text
        references: none
        tools: none
        maximum-calls: 0
        maximum-rounds: 1
        maximum-input-bytes: 16000
        maximum-output-bytes: 2000
        deadline-ms: 1000
        ---
        Exact private guidance.
        """;
    private readonly string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        ".net-test-artifacts", "agent-authoring", Guid.NewGuid().ToString("N")));
    private string Target => Path.Combine(root, "OwnedAgents-v1", "example.agent.md");
    private WindowsOwnedAgentProfileStore Create()
    {
        Directory.CreateDirectory(root);
        return new(new Paths(root));
    }
    private static Task<AgentProfileWriteResult> Save(WindowsOwnedAgentProfileStore store, OwnedAgentProfileDestination destination,
        Func<bool>? eligible = null, CancellationToken token = default) =>
        store.Create(destination, OwnedAgentProfileDocument.Parse("example", Source), eligible ?? (() => true),
            operation => Task.FromResult(operation()), token);

    [Fact]
    public async Task ExactNewOriginalPublishesAndReopensWithPinnedDurableIdentity()
    {
        var store = Create();
        var token = TestContext.Current.CancellationToken;
        (await store.Read("example", token)).Should().BeNull();
        var destination = await store.PrepareNew("example", token);
        File.Exists(Target).Should().BeFalse();
        var saved = await Save(store, destination, token: token);
        saved.State.Should().Be(AgentProfileWriteState.Saved);
        saved.Snapshot!.Destination.Should().Be(destination);
        File.ReadAllBytes(Target).Should().Equal(saved.Snapshot.Document.Document.CopyBytes());
        var opened = await store.Read("example", token);
        opened!.FileIdentity.Should().Be(saved.Snapshot.FileIdentity);
        opened.Document.Document.CopyBytes().Should().Equal(saved.Snapshot.Document.Document.CopyBytes());
        (await store.List(token)).Should().Equal("example");
        Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").Should().BeEmpty();
        var duplicate = () => Save(store, destination, token: token);
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
        File.ReadAllBytes(Target).Should().Equal(saved.Snapshot.Document.Document.CopyBytes());
    }

    [Fact]
    public async Task AtomicCreateNeverReplacesConcurrentDestination()
    {
        var store = Create();
        var destination = await store.PrepareNew("example", TestContext.Current.CancellationToken);
        store.AfterFinalComparison = () => File.WriteAllText(Target, "Concurrent original");
        var save = () => Save(store, destination);
        await save.Should().ThrowAsync<InvalidOperationException>();
        File.ReadAllText(Target).Should().Be("Concurrent original");
        Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostPublicationRevocationAndReadbackFailureAreNotRolledBack(bool corrupt)
    {
        var store = Create();
        var destination = await store.PrepareNew("example", TestContext.Current.CancellationToken);
        var eligible = true;
        store.AfterPublication = () => { if (corrupt) { File.WriteAllText(Target, "Changed after publication"); } else { eligible = false; } };
        var save = () => Save(store, destination, () => eligible);
        await save.Should().ThrowAsync<AgentProfilePublicationUnconfirmedException>();
        File.Exists(Target).Should().BeTrue();
    }

    [Fact]
    public async Task RootReplacementAndForgedDestinationsCannotWrite()
    {
        var store = Create();
        var destination = await store.PrepareNew("example", TestContext.Current.CancellationToken);
        var forged = () => Save(store, destination with { DirectoryIdentity = "other" });
        await forged.Should().ThrowAsync<InvalidOperationException>();
        var wrong = () => Save(store, destination with { Name = "other" });
        await wrong.Should().ThrowAsync<InvalidDataException>();
        Directory.Move(Path.GetDirectoryName(Target)!, Path.Combine(root, "retained"));
        using var identity = WindowsIdentity.GetCurrent();
        Kora.Windows.Storage.RestrictedStorageDirectory.CreateRestrictedDirectory(Path.GetDirectoryName(Target)!, identity.User!);
        var replaced = () => Save(store, destination);
        await replaced.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("The owned source directory was replaced.*");
        File.Exists(Target).Should().BeFalse();
        Directory.GetFiles(Path.GetDirectoryName(Target)!).Should().BeEmpty();
    }

    [Fact]
    public async Task EligibilityIsRecheckedAfterFinalComparisonAndStagingIsRemoved()
    {
        var store = Create();
        var destination = await store.PrepareNew("example", TestContext.Current.CancellationToken);
        var eligible = true;
        store.AfterFinalComparison = () => eligible = false;
        var save = () => Save(store, destination, () => eligible);
        await save.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(Target).Should().BeFalse();
        Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Example")]
    [InlineData("../example")]
    [InlineData("example:stream")]
    [InlineData("con")]
    public async Task InvalidNamesNeverBecomeDestinations(string name)
    {
        var store = Create();
        var prepare = () => store.PrepareNew(name, TestContext.Current.CancellationToken);
        await prepare.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Target).Should().BeFalse();
    }

    private static Task<AgentProfileWriteResult> Update(WindowsOwnedAgentProfileStore store,
        OwnedAgentProfileSnapshot original, string source, Func<bool>? eligible = null, CancellationToken token = default) =>
        store.Update(original, OwnedAgentProfileDocument.Parse("example", source), eligible ?? (() => true),
            operation => Task.FromResult(operation()), token);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingV1AndV4UpdateSameCanonicalDestinationAndReopenExactBytes(bool combined)
    {
        var store = Create();
        var token = TestContext.Current.CancellationToken;
        var source = combined ? Source.Replace("kora-agent-format: 1", "kora-agent-format: 4", StringComparison.Ordinal)
            .Replace("references: none", "references: disk.kora-profile.instruction.example", StringComparison.Ordinal)
            .Replace("tools: none", "tools: application.get_version", StringComparison.Ordinal)
            .Replace("maximum-calls: 0", "maximum-calls: 1", StringComparison.Ordinal) : Source;
        var destination = await store.PrepareNew("example", token);
        var original = (await store.Create(destination, OwnedAgentProfileDocument.Parse("example", source), () => true,
            operation => Task.FromResult(operation()), token)).Snapshot!;
        var next = source + "\nChanged inert source.";
        var updated = await Update(store, original, next, token: token);
        updated.State.Should().Be(AgentProfileWriteState.Saved);
        updated.Snapshot!.Destination.Should().Be(original.Destination);
        updated.Snapshot.FileIdentity.Should().NotBe(original.FileIdentity);
        File.ReadAllBytes(Target).Should().Equal(updated.Snapshot.Document.Document.CopyBytes());
        var reopened = await store.Read("example", token);
        reopened!.FileIdentity.Should().Be(updated.Snapshot.FileIdentity);
        reopened.Document.Document.Text.Should().Be(next);
        (await store.List(token)).Should().Equal("example");
        (await Update(store, reopened, next, token: token)).State.Should().Be(AgentProfileWriteState.Unchanged);
        Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task SavingDoesNotPublishCatalogueAuthorityOrRetargetAnOldImmutableSelection()
    {
        var store = Create();
        var original = (await Save(store, await store.PrepareNew("example", TestContext.Current.CancellationToken), token: TestContext.Current.CancellationToken)).Snapshot!;
        var admitted = original.Document.Profile;
        using var catalogue = new ArtifactCatalogue([original.Document.Document.Definition with
        { UseControlId = ArtifactUseIdentity.File(Target) }]);
        var before = catalogue.Candidates;
        var resolver = new NamedAgentResolver(catalogue, (_, _) => Task.FromResult(Target));
        var pending = await resolver.ResolveAsync("example", () => true, TestContext.Current.CancellationToken);
        var updated = await Update(store, original, Source + "\nChanged inert source", token: TestContext.Current.CancellationToken);
        catalogue.Candidates.Should().BeSameAs(before);
        var stale = () => pending.ValidateSource(updated.Snapshot!.Document.Document);
        stale.Should().Throw<InvalidOperationException>();
        admitted.Document.CopyBytes().Should().Equal(original.Document.Document.CopyBytes());
        admitted.Document.Text.Should().Be(Source);
        (await store.List(TestContext.Current.CancellationToken)).Should().Equal("example");
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("identity")]
    [InlineData("missing")]
    [InlineData("final-bytes")]
    [InlineData("final-identity")]
    public async Task StaleBaseNeverWritesAndExplicitObservedOverwriteCanConflictAgain(string scenario)
    {
        var store = Create();
        var original = (await Save(store, await store.PrepareNew("example", TestContext.Current.CancellationToken), token: TestContext.Current.CancellationToken)).Snapshot!;
        void Change()
        {
            if (scenario.Contains("identity", StringComparison.Ordinal))
            {
                var other = Path.Combine(root, "replacement.agent.md");
                WindowsOwnedInstructionStoreTests.WriteOwnedFile(other, original.Document.Document.CopyBytes());
                File.Move(other, Target, overwrite: true);
            }
            else if (scenario is "missing") { File.Delete(Target); }
            else { File.WriteAllText(Target, Source + "\nExternal edit"); }
        }
        if (scenario.StartsWith("final-", StringComparison.Ordinal)) { store.AfterFinalComparison = Change; }
        else { Change(); }
        var conflict = await Update(store, original, Source + "\nKora draft", token: TestContext.Current.CancellationToken);
        conflict.State.Should().Be(AgentProfileWriteState.Conflict);
        store.AfterFinalComparison = null;
        if (scenario is "missing")
        {
            conflict.Snapshot.Should().BeNull();
            File.Exists(Target).Should().BeFalse();
        }
        else
        {
            conflict.Snapshot.Should().NotBeNull();
            File.WriteAllText(Target, Source + "\nAnother external change");
            var again = await Update(store, conflict.Snapshot!, Source + "\nKora draft", token: TestContext.Current.CancellationToken);
            again.State.Should().Be(AgentProfileWriteState.Conflict);
            File.ReadAllText(Target).Should().Be(Source + "\nAnother external change");
            var saved = await Update(store, again.Snapshot!, Source + "\nKora draft", token: TestContext.Current.CancellationToken);
            saved.State.Should().Be(AgentProfileWriteState.Saved);
        }
        Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("path")]
    [InlineData("name")]
    [InlineData("directory")]
    [InlineData("ancestors")]
    [InlineData("foreign-store")]
    [InlineData("replaced-root")]
    public async Task ForgedOrReplacedScopeNeverUpdatesAnotherOriginal(string scenario)
    {
        var store = Create();
        var original = (await Save(store, await store.PrepareNew("example", TestContext.Current.CancellationToken), token: TestContext.Current.CancellationToken)).Snapshot!;
        var expected = original with { Destination = scenario switch
        {
            "path" => original.Destination with { DisplayPath = Path.Combine(root, "outside.agent.md") },
            "name" => original.Destination with { Name = "other" },
            "directory" => original.Destination with { DirectoryIdentity = "other" },
            "ancestors" => original.Destination with { RootBindingIdentity = "other" },
            _ => original.Destination,
        } };
        if (scenario is "foreign-store") { store = new(new Paths(Path.Combine(root, "other"))); }
        if (scenario is "replaced-root")
        {
            Directory.Move(Path.GetDirectoryName(Target)!, Path.Combine(root, "retained"));
            using var identity = WindowsIdentity.GetCurrent();
            Kora.Windows.Storage.RestrictedStorageDirectory.CreateRestrictedDirectory(Path.GetDirectoryName(Target)!, identity.User!);
        }
        var write = () => Update(store, expected, Source + "\nDraft");
        if (scenario is "directory" or "ancestors") { (await write()).State.Should().Be(AgentProfileWriteState.Conflict); }
        else { await write.Should().ThrowAsync<InvalidDataException>(); }
        if (scenario is "replaced-root") { File.Exists(Target).Should().BeFalse(); }
        else { File.ReadAllText(Target).Should().Be(Source); }
        File.Exists(Path.Combine(root, "outside.agent.md")).Should().BeFalse();
    }

    [Theory]
    [InlineData("hardlink")]
    [InlineData("symlink")]
    [InlineData("alias")]
    public async Task LinkOrAliasAtFinalComparisonCannotEscapeOrChangeOutsideBytes(string scenario)
    {
        var store = Create();
        var original = (await Save(store, await store.PrepareNew("example", TestContext.Current.CancellationToken), token: TestContext.Current.CancellationToken)).Snapshot!;
        var outside = Path.Combine(root, "outside.agent.md");
        WindowsOwnedInstructionStoreTests.WriteOwnedFile(outside, original.Document.Document.CopyBytes());
        store.AfterFinalComparison = () =>
        {
            File.Delete(Target);
            if (scenario is "hardlink") { CreateHardLink(Target, outside, nint.Zero).Should().BeTrue(); }
            else if (scenario is "symlink") { File.CreateSymbolicLink(Target, outside); }
            else { WindowsOwnedInstructionStoreTests.WriteOwnedFile(Path.Combine(Path.GetDirectoryName(Target)!, "Example.agent.md"), original.Document.Document.CopyBytes()); }
        };
        var write = () => Update(store, original, Source + "\nDraft");
        await write.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllText(outside).Should().Be(Source);
        Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task PinnedAgentParentCannotBeReplacedDuringPublication()
    {
        var store = Create();
        var original = (await Save(store, await store.PrepareNew("example", TestContext.Current.CancellationToken), token: TestContext.Current.CancellationToken)).Snapshot!;
        var blocked = false;
        store.AfterFinalComparison = () =>
        {
            try { Directory.Move(Path.GetDirectoryName(Target)!, Path.Combine(root, "redirected")); }
            catch (IOException) { blocked = true; }
        };
        (await Update(store, original, Source + "\nDraft", token: TestContext.Current.CancellationToken)).State.Should().Be(AgentProfileWriteState.Saved);
        blocked.Should().BeTrue();
        Directory.Exists(Path.Combine(root, "redirected")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateCancellationBeforeOrAfterPublicationIsTruthfulAndCleansStaging(bool afterPublication)
    {
        var store = Create();
        var original = (await Save(store, await store.PrepareNew("example", TestContext.Current.CancellationToken), token: TestContext.Current.CancellationToken)).Snapshot!;
        using var cancellation = new CancellationTokenSource();
        if (afterPublication) { store.AfterPublication = cancellation.Cancel; }
        else { store.AfterFinalComparison = cancellation.Cancel; }
        var write = () => Update(store, original, Source + "\nDraft", token: cancellation.Token);
        if (afterPublication) { await write.Should().ThrowAsync<AgentProfilePublicationUnconfirmedException>(); }
        else { await write.Should().ThrowAsync<OperationCanceledException>(); }
        File.ReadAllText(Target).Should().Be(afterPublication ? Source + "\nDraft" : Source);
        Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateRevocationOrReadbackFailureNeverClaimsSuccess(bool afterPublication)
    {
        var store = Create();
        var original = (await Save(store, await store.PrepareNew("example", TestContext.Current.CancellationToken), token: TestContext.Current.CancellationToken)).Snapshot!;
        var eligible = true;
        if (afterPublication) { store.AfterPublication = () => File.WriteAllText(Target, "Unconfirmed external bytes"); }
        else { store.AfterFinalComparison = () => eligible = false; }
        var write = () => Update(store, original, Source + "\nDraft", () => eligible);
        if (afterPublication) { await write.Should().ThrowAsync<AgentProfilePublicationUnconfirmedException>(); }
        else { await write.Should().ThrowAsync<InvalidOperationException>(); File.ReadAllText(Target).Should().Be(Source); }
        Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task FailedNativeReplacementPreservesOriginalAndCleansStagingWithoutRetry()
    {
        var store = Create();
        var token = TestContext.Current.CancellationToken;
        var original = (await Save(store, await store.PrepareNew("example", token), token: token)).Snapshot!;
        File.SetAttributes(Target, File.GetAttributes(Target) | FileAttributes.ReadOnly);
        try
        {
            var write = () => Update(store, original, Source + "\nDraft", token: token);
            await write.Should().ThrowAsync<IOException>();
            File.ReadAllBytes(Target).Should().Equal(original.Document.Document.CopyBytes());
            Directory.GetFiles(Path.GetDirectoryName(Target)!, "*.tmp").Should().BeEmpty();
        }
        finally { File.SetAttributes(Target, File.GetAttributes(Target) & ~FileAttributes.ReadOnly); }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string link, string existing, nint security);

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    private sealed record Paths(string RoamingRoot) : IApplicationDataPaths { public string LocalRoot => RoamingRoot; }
}
