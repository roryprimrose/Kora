using System.Security.Principal;

using AwesomeAssertions;

using Kora.Core.Agents;
using Kora.Core.Dependencies;
using Kora.Windows.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed class WindowsOwnedAgentProfileStoreTests : IDisposable
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

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    private sealed record Paths(string RoamingRoot) : IApplicationDataPaths { public string LocalRoot => RoamingRoot; }
}
