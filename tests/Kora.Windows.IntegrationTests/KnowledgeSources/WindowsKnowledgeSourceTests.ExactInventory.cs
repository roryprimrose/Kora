using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.KnowledgeSources;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.KnowledgeSources;

namespace Kora.Windows.IntegrationTests.KnowledgeSources;

public sealed partial class WindowsKnowledgeSourceTests
{
    public static TheoryData<SourceOperation, InventoryField> ChangedInventories =>
        new(from operation in Enum.GetValues<SourceOperation>()
            from changedField in Enum.GetValues<InventoryField>()
            select (operation, changedField));

    [Theory]
    [MemberData(nameof(ChangedInventories))]
    public async Task CompleteObservedInventoryMustMatchBeforeAnyRetainedReadOrMutation(
        SourceOperation operation, InventoryField field)
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var input = Snapshot();
        var source = await store.PublishAsync(null, "notes", input.Snapshot, input.Bytes, Token);
        if (operation == SourceOperation.Publish) { source = await store.BeginRefreshAsync(source, Token); }
        var changed = ChangeObservation(source, field);
        var database = Path.Combine(fixture.LocalRoot, "KnowledgeSourcesV1", "sources.db");
        var before = await File.ReadAllBytesAsync(database, Token);

        await FluentActions.Awaiting(() => ApplySourceOperationAsync(store, operation, changed, input))
            .Should().ThrowAsync<InvalidOperationException>();

        (await File.ReadAllBytesAsync(database, Token)).Should().Equal(before);
        var observed = (await new WindowsKnowledgeSourceStore(fixture).ListAsync(Token)).Should().ContainSingle().Which;
        observed.Should().Be(source with { Folder = observed.Folder });
        observed.Folder.Matches(source.Folder).Should().BeTrue();
        if (source.State == KnowledgeSourceState.Enabled)
        {
            (await store.ReadAsync(source, Token)).Files[0].Text.Should().Be("alpha notes");
        }
    }

    [Theory]
    [InlineData(SourceOperation.Inspect)]
    [InlineData(SourceOperation.Refresh)]
    [InlineData(SourceOperation.Disable)]
    [InlineData(SourceOperation.Remove)]
    [InlineData(SourceOperation.Publish)]
    public async Task SameVersionDurableInventoryDriftRequiresReloadInsteadOfSubstitutingTheCurrentRow(
        SourceOperation operation)
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var input = Snapshot();
        var source = await store.PublishAsync(null, "notes", input.Snapshot, input.Bytes, Token);
        if (operation == SourceOperation.Publish) { source = await store.BeginRefreshAsync(source, Token); }
        RewriteStorage(fixture, info => info["Name"] = JsonValue.Create("changed inventory"));
        var database = Path.Combine(fixture.LocalRoot, "KnowledgeSourcesV1", "sources.db");
        var before = await File.ReadAllBytesAsync(database, Token);
        var reopened = new WindowsKnowledgeSourceStore(fixture);

        await FluentActions.Awaiting(() => ApplySourceOperationAsync(reopened, operation, source, input))
            .Should().ThrowAsync<InvalidOperationException>();

        (await File.ReadAllBytesAsync(database, Token)).Should().Equal(before);
        var current = (await reopened.ListAsync(Token)).Should().ContainSingle().Which;
        current.Name.Should().Be("changed inventory");
        current.Version.Should().Be(source.Version);
        current.State.Should().Be(source.State);
        await ApplySourceOperationAsync(reopened, operation, current, input);
    }

    [Theory]
    [InlineData(SourceOperation.Inspect)]
    [InlineData(SourceOperation.Refresh)]
    [InlineData(SourceOperation.Disable)]
    [InlineData(SourceOperation.Remove)]
    [InlineData(SourceOperation.Publish)]
    public async Task StructurallyIdenticalObservationSurvivesReopenAndCompletesTheSelectedOperation(
        SourceOperation operation)
    {
        using var fixture = new OwnedStorageFixture();
        var store = new WindowsKnowledgeSourceStore(fixture);
        var input = Snapshot();
        var source = await store.PublishAsync(null, "notes", input.Snapshot, input.Bytes, Token);
        if (operation == SourceOperation.Publish) { source = await store.BeginRefreshAsync(source, Token); }
        var equivalent = source with
        {
            Folder = new(source.Folder.CanonicalPath, source.Folder.DirectoryIdentity, source.Folder.Scope,
                source.Folder.Directories.ToArray(), source.Folder.Files.Select(file => file with { }).ToArray()),
        };
        equivalent.Folder.Should().NotBeSameAs(source.Folder);
        var reopened = new WindowsKnowledgeSourceStore(fixture);

        await ApplySourceOperationAsync(reopened, operation, equivalent, input);

        var rows = await reopened.ListAsync(Token);
        if (operation == SourceOperation.Remove) { rows.Should().BeEmpty(); return; }
        var current = rows.Should().ContainSingle().Which;
        current.Version.Should().Be(source.Version + (operation == SourceOperation.Inspect ? 0 : 1));
        current.State.Should().Be(operation switch
        {
            SourceOperation.Refresh => KnowledgeSourceState.Unavailable,
            SourceOperation.Disable => KnowledgeSourceState.Disabled,
            _ => KnowledgeSourceState.Enabled,
        });
        if (current.State == KnowledgeSourceState.Enabled)
        {
            (await reopened.ReadAsync(current, Token)).Files[0].Text.Should().Be("alpha notes");
        }
    }

    private static Task ApplySourceOperationAsync(WindowsKnowledgeSourceStore store, SourceOperation operation,
        KnowledgeSource source, (KnowledgeSourceRevision Snapshot, IReadOnlyList<ReadOnlyMemory<byte>> Bytes) input) =>
        operation switch
        {
            SourceOperation.Inspect => store.ReadAsync(source, Token),
            SourceOperation.Refresh => store.BeginRefreshAsync(source, Token),
            SourceOperation.Disable => store.DisableAsync(source, Token),
            SourceOperation.Remove => store.RemoveAsync(source, Token),
            SourceOperation.Publish => store.PublishAsync(source, source.Name, input.Snapshot, input.Bytes, Token),
            _ => throw new InvalidOperationException(),
        };

    private static KnowledgeSource ChangeObservation(KnowledgeSource source, InventoryField field)
    {
        var folder = source.Folder;
        var file = folder.Files.Single();
        var path = field == InventoryField.RootPath ? @"C:\team" : folder.CanonicalPath;
        var identity = field == InventoryField.RootIdentity ? "different-root" : folder.DirectoryIdentity;
        var scope = field is InventoryField.Scope or InventoryField.Directories
            ? KnowledgeSourceScope.BoundedDescendants : folder.Scope;
        KnowledgeSourceDirectory[] directories = field == InventoryField.Directories
            ? [new(path, identity), new(path + @"\Empty", "empty-directory")] : [new(path, identity)];
        var changedFile = field switch
        {
            InventoryField.FilePath => file with { CanonicalPath = @"C:\Team\other.md" },
            InventoryField.FileIdentity => file with { FileIdentity = "different-file" },
            InventoryField.FileBytes => file with { ByteLength = file.ByteLength + 1 },
            InventoryField.FileTime => file with { LastWrite = file.LastWrite.AddSeconds(1) },
            _ => file,
        };
        return source with
        {
            Id = field == InventoryField.Id ? Guid.NewGuid() : source.Id,
            Version = field == InventoryField.Version ? source.Version + 1 : source.Version,
            Name = field == InventoryField.Name ? "Notes" : source.Name,
            State = field == InventoryField.State ? KnowledgeSourceState.Disabled : source.State,
            ChangedAt = field == InventoryField.ChangedAt ? source.ChangedAt.AddSeconds(1) : source.ChangedAt,
            StoredBytes = field == InventoryField.StoredBytes ? source.StoredBytes + 1 : source.StoredBytes,
            Folder = field == InventoryField.MissingFolder ? null! : new(path, identity, scope, directories, [changedFile]),
        };
    }

    public enum SourceOperation { Inspect, Refresh, Disable, Remove, Publish }
    public enum InventoryField
    {
        Id, Version, Name, State, ChangedAt, StoredBytes, RootPath, RootIdentity, Scope, Directories,
        FilePath, FileIdentity, FileBytes, FileTime, MissingFolder,
    }
}
