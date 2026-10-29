using System.Security.Principal;
using System.Text;

using AwesomeAssertions;

using Kora.Core.Customizations;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Windows.Artifacts;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsCustomizationStoreTests : IDisposable
{
    private readonly string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        ".net-test-artifacts", "customizations", Guid.NewGuid().ToString("N")));
    private WindowsCustomizationStore Create() => new(new Paths(root), Path.Combine(root, "editor-profile"), Path.Combine(root, "user-profile"));
    private sealed class DirectoryPreferences(ArtifactDirectorySettings settings) : IArtifactDirectoryPreferences
    {
        private ArtifactDirectorySettings current = settings;
        public ArtifactDirectorySettings Load() => current;
        public void Save(ArtifactDirectorySettings value) => current = value;
    }

    [Fact]
    public async Task Configured_sources_are_shared_by_native_and_metadata_inventory_and_removed_sources_cannot_be_read()
    {
        var paths = new Paths(root);
        var extra = Path.Combine(root, "custom-agents");
        Directory.CreateDirectory(root);
        using var identity = WindowsIdentity.GetCurrent();
        RestrictedStorageDirectory.CreateRestrictedDirectory(extra, identity.User!);
        var file = Path.Combine(extra, "helper.agent.md");
        File.WriteAllText(file, "---\nname: Helper\ndescription: Custom agent\n---\nCustom agent.");
        var preferences = new DirectoryPreferences(new([], [], [], [extra], [], [], false));
        var store = new WindowsCustomizationStore(paths, Path.Combine(root, "editor-profile"),
            Path.Combine(root, "user-profile"), preferences);
        var source = (await store.List(TestContext.Current.CancellationToken)).Should().ContainSingle().Subject;
        source.Scope.Should().Be(CustomizationScope.User);
        (await store.ListMetadataSources(TestContext.Current.CancellationToken)).Should().Equal(source);
        (await store.ReadMetadata(source, TestContext.Current.CancellationToken)).Name.Should().Be("Helper");
        preferences.Save(new([], [], [], [], [], [], false));
        (await store.List(TestContext.Current.CancellationToken)).Should().BeEmpty();
        await ((Func<Task>)(() => store.ReadMetadata(source, TestContext.Current.CancellationToken)))
            .Should().ThrowAsync<InvalidOperationException>();
        File.Exists(file).Should().BeTrue();
    }
    private sealed record Paths(string Root) : IApplicationDataPaths
    {
        public string LocalRoot => Path.Combine(Root, "local");
        public string RoamingRoot => Path.Combine(Root, "roaming");
    }

    private static CustomizationFile Primary(CustomizationSource source, string body = "") =>
        new(source.PrimaryFile, Encoding.UTF8.GetBytes(CustomizationLayout.Template(source.Kind, source.Name) + body));
    private static Task<CustomizationSnapshot> Save(WindowsCustomizationStore store, CustomizationSource source,
        CustomizationSnapshot? before, params CustomizationFile[] files) =>
        store.Save(source, before, files, () => true, operation => Task.FromResult(operation()), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(CustomizationKind.Skill, CustomizationScope.Kora)]
    [InlineData(CustomizationKind.Instruction, CustomizationScope.Kora)]
    [InlineData(CustomizationKind.Agent, CustomizationScope.Kora)]
    [InlineData(CustomizationKind.McpServer, CustomizationScope.Kora)]
    [InlineData(CustomizationKind.Prompt, CustomizationScope.Kora)]
    [InlineData(CustomizationKind.Skill, CustomizationScope.User)]
    [InlineData(CustomizationKind.Agent, CustomizationScope.User)]
    [InlineData(CustomizationKind.McpServer, CustomizationScope.User)]
    public async Task Complete_create_read_edit_conflict_and_reopen_roundtrip(CustomizationKind kind, CustomizationScope scope)
    {
        var store = Create();
        var source = await store.Create(kind, scope, "example", TestContext.Current.CancellationToken);
        var saved = await Save(store, source, null, Primary(source));
        saved.Files.Single().Text.Should().Contain(kind == CustomizationKind.McpServer ? "https://example.invalid" : "Write the guidance");
        var content = saved.Files.Single().WithText(kind == CustomizationKind.McpServer
            ? "{\"url\":\"https://changed.invalid/mcp\"}" : saved.Files.Single().Text + "Changed.");
        var edited = await Save(store, source, saved, content);
        edited.Revision.Should().NotBe(saved.Revision);
        var conflict = () => Save(store, source, saved, Primary(source));
        await conflict.Should().ThrowAsync<InvalidOperationException>();
        var reopened = Create();
        var listed = await reopened.List(TestContext.Current.CancellationToken);
        var selected = listed.Single(item => string.Equals(item.Id, source.Id, StringComparison.Ordinal));
        (await reopened.Read(selected, TestContext.Current.CancellationToken)).Files.Single().Text.Should().Be(content.Text);
    }

    [Fact]
    public async Task Interrupted_user_package_transaction_survives_restart_and_requires_exact_recovery_review()
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.Skill, CustomizationScope.User, "example", TestContext.Current.CancellationToken);
        store.AfterComponentPublished = _ => throw new IOException("Simulated interrupted publication");
        await ((Func<Task>)(() => Save(store, source, null, Primary(source), new("references\\notes.md", Encoding.UTF8.GetBytes("Reference")))))
            .Should().ThrowAsync<IOException>();
        var restarted = Create();
        var pending = (await restarted.List(TestContext.Current.CancellationToken)).Single(item => string.Equals(item.Id, source.Id, StringComparison.Ordinal));
        await ((Func<Task>)(() => restarted.Read(pending, TestContext.Current.CancellationToken))).Should().ThrowAsync<InvalidOperationException>();
        var recovery = await restarted.ReadRecovery(pending, TestContext.Current.CancellationToken);
        recovery.RecoveryDetails.Should().Contain("Original complete").And.Contain("Current complete").And.Contain("Proposed complete");
        var completed = await Save(restarted, pending, recovery, recovery.Files.ToArray());
        completed.Files.Select(file => file.Name).Should().Equal("SKILL.md", "references\\notes.md");
        (await restarted.Read(pending, TestContext.Current.CancellationToken)).RecoveryDetails.Should().BeNull();
    }

    [Fact]
    public async Task Component_add_rename_remove_publishes_the_exact_complete_file_set()
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.Skill, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        var first = await Save(store, source, null, Primary(source), new("references\\old.md", Encoding.UTF8.GetBytes("Retained")));
        var changed = await Save(store, source, first, Primary(source), new("references\\new.md", Encoding.UTF8.GetBytes("Retained")));
        changed.Files.Select(file => file.Name).Should().Equal("SKILL.md", "references\\new.md");
        File.Exists(Path.Combine(Path.GetDirectoryName(source.Location)!, "references", "old.md")).Should().BeFalse();
        var removed = await Save(store, source, changed, Primary(source));
        removed.Files.Should().ContainSingle();
    }

    [Fact]
    public async Task File_directory_conflicts_are_rejected_before_any_component_or_journal_is_published()
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.Skill, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        Func<Task> save = () => Save(store, source, null, Primary(source),
            new("references", Encoding.UTF8.GetBytes("File")), new("references\\notes.md", Encoding.UTF8.GetBytes("Child")));
        await save.Should().ThrowAsync<InvalidDataException>();
        File.Exists(source.Location).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(source.Location)).Should().BeFalse();
        Directory.Exists(Path.Combine(root, "local", "DefinitionTransactions")).Should().BeFalse();
    }

    [Theory]
    [InlineData("notes.md", "Notes.md", false)]
    [InlineData("Notes.md", "notes.md", false)]
    [InlineData("references\\notes.md", "references\\Notes.md", true)]
    public async Task Case_only_component_rename_publishes_exact_casing_and_recovers_after_deletion(
        string before, string after, bool interrupt)
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.Skill, CustomizationScope.User, "example", TestContext.Current.CancellationToken);
        var first = await Save(store, source, null, Primary(source), new(before, Encoding.UTF8.GetBytes("Retained")));
        if (interrupt) { store.AfterComponentPublished = _ => throw new IOException("Interrupted after case-only deletion"); }
        CustomizationSnapshot renamed;
        if (interrupt)
        {
            await ((Func<Task>)(() => Save(store, source, first, Primary(source), new(after, Encoding.UTF8.GetBytes("Retained")))))
                .Should().ThrowAsync<IOException>();
            File.Exists(Path.Combine(Path.GetDirectoryName(source.Location)!, before)).Should().BeFalse();
            var restarted = Create();
            var pending = (await restarted.List(TestContext.Current.CancellationToken)).Single(item =>
                string.Equals(item.Id, source.Id, StringComparison.Ordinal));
            var recovery = await restarted.ReadRecovery(pending, TestContext.Current.CancellationToken);
            renamed = await Save(restarted, pending, recovery, recovery.Files.ToArray());
        }
        else { renamed = await Save(store, source, first, Primary(source), new(after, Encoding.UTF8.GetBytes("Retained"))); }
        renamed.Files.Select(file => file.Name).Should().Equal(new[] { "SKILL.md", after }.Order(StringComparer.Ordinal));
        renamed.Files.Single(file => string.Equals(file.Name, after, StringComparison.Ordinal)).Text.Should().Be("Retained");
        Directory.GetFiles(Path.Combine(root, "local", "DefinitionTransactions")).Should().BeEmpty();
    }

    [Fact]
    public async Task Mcp_save_changes_only_the_selected_server_bytes()
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.McpServer, CustomizationScope.User, "example", TestContext.Current.CancellationToken);
        await Save(store, source, null, Primary(source));
        const string external = "{/*keep*/\"inputs\":[],\"servers\":{\"example\":{\"url\":\"https://old.invalid\"},\"other\":{\"command\":\"untouched\"}},\"tail\":true}";
        File.WriteAllText(source.Location, external);
        var current = await store.Read(source, TestContext.Current.CancellationToken);
        await Save(store, source, current, new CustomizationFile("server.json", Encoding.UTF8.GetBytes("{\"url\":\"https://new.invalid\"}")));
        File.ReadAllText(source.Location).Should().Be(external.Replace("https://old.invalid", "https://new.invalid", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_rejects_unknown_and_duplicate_journal_properties(bool duplicate)
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.Skill, CustomizationScope.User, "example", TestContext.Current.CancellationToken);
        store.AfterComponentPublished = _ => throw new IOException("Interrupted");
        await ((Func<Task>)(() => Save(store, source, null, Primary(source)))).Should().ThrowAsync<IOException>();
        var journal = Directory.GetFiles(Path.Combine(root, "local", "DefinitionTransactions")).Single();
        var content = File.ReadAllText(journal);
        File.WriteAllText(journal, content.Insert(1, duplicate ? "\"DirectoryIdentity\":\"invalid\"," : "\"Unexpected\":true,"));
        await ((Func<Task>)(() => Create().List(TestContext.Current.CancellationToken))).Should().ThrowAsync<InvalidDataException>();
        await ((Func<Task>)(() => store.ReadRecovery(source, TestContext.Current.CancellationToken))).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Recovery_does_not_overwrite_an_external_edit()
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.Skill, CustomizationScope.User, "example", TestContext.Current.CancellationToken);
        var saved = await Save(store, source, null, Primary(source));
        store.AfterComponentPublished = _ => throw new IOException("Interrupted");
        var interrupted = () => Save(store, source, saved, Primary(source, "Changed."),
            new CustomizationFile("note.md", Encoding.UTF8.GetBytes("Note")));
        await interrupted.Should().ThrowAsync<IOException>();
        File.WriteAllText(source.Location, "An external change");
        var recovery = await store.ReadRecovery(source, TestContext.Current.CancellationToken);
        store.AfterComponentPublished = null;
        await ((Func<Task>)(() => Save(store, source, recovery, recovery.Files.ToArray()))).Should().ThrowAsync<InvalidOperationException>();
        File.ReadAllText(source.Location).Should().Be("An external change");
        Directory.GetFiles(Path.Combine(root, "local", "DefinitionTransactions")).Should().ContainSingle();
    }

    [Fact]
    public async Task Empty_directory_inventory_is_bounded_without_publishing_changes()
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.Skill, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        await Save(store, source, null, Primary(source));
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new InvalidOperationException("The private fixture owner is unavailable.");
        for (var index = 0; index < 129; index++)
        {
            RestrictedStorageDirectory.CreateRestrictedDirectory(
                Path.Combine(Path.GetDirectoryName(source.Location)!, "directory" + index), owner);
        }
        await ((Func<Task>)(() => store.Read(source, TestContext.Current.CancellationToken))).Should().ThrowAsync<InvalidDataException>();
        Directory.GetFiles(Path.Combine(root, "local", "DefinitionTransactions")).Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }
}