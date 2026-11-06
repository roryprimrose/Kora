using System.Text;

using AwesomeAssertions;

using Kora.Core.Customizations;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsCustomizationStoreTests
{
    [Theory]
    [InlineData(CustomizationKind.Instruction)]
    [InlineData(CustomizationKind.Prompt)]
    [InlineData(CustomizationKind.Agent)]
    public async Task ExactSharedDeletionRemovesOnlyTheReviewedFileAndNeverItsDirectory(CustomizationKind kind)
    {
        var store = Create();
        var source = await store.Create(kind, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        var saved = await Save(store, source, null, Primary(source));
        var other = await store.Create(kind, CustomizationScope.Kora, "unrelated", TestContext.Current.CancellationToken);
        var untouched = await Save(store, other, null, Primary(other));
        await store.Delete(saved, () => true, action => { action(); return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        File.Exists(source.Location).Should().BeFalse();
        Directory.Exists(Path.GetDirectoryName(source.Location)).Should().BeTrue();
        File.ReadAllBytes(other.Location).Should().Equal(untouched.Files.Single().CopyBytes());
        (await store.List(TestContext.Current.CancellationToken)).Should().NotContain(source);
        Directory.GetFiles(Path.Combine(root, "local", "DefinitionTransactions")).Should().BeEmpty();
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("replacement")]
    [InlineData("root")]
    [InlineData("privacy")]
    [InlineData("cancel")]
    [InlineData("final-privacy")]
    [InlineData("write-race")]
    [InlineData("replace-race")]
    [InlineData("symlink")]
    [InlineData("hardlink")]
    [InlineData("case-alias")]
    [InlineData("unknown")]
    [InlineData("outside")]
    [InlineData("recovery")]
    public async Task SharedDeletionRejectsStaleAndUnsafeTargetsWithoutDeletingReplacements(string failure)
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.Instruction, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        var saved = await Save(store, source, null, Primary(source));
        var originalBytes = File.ReadAllBytes(source.Location);
        var moved = source.Location + ".original";
        var current = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        switch (failure)
        {
            case "changed": File.WriteAllText(source.Location, "Externally changed"); break;
            case "missing": File.Delete(source.Location); break;
            case "replacement":
                File.Move(source.Location, moved); File.Copy(moved, source.Location); break;
            case "root":
                var directory = Path.GetDirectoryName(source.Location)!;
                Directory.Move(directory, directory + "-old");
                Directory.CreateDirectory(directory);
                WindowsOwnedInstructionStoreTests.WriteOwnedFile(source.Location, originalBytes);
                break;
            case "privacy": current = false; break;
            case "cancel": cancellation.Cancel(); break;
            case "final-privacy": store.BeforeDeletionComparison = () => current = false; break;
            case "write-race": store.BeforeDeletionComparison = () => File.WriteAllText(source.Location, "Unreviewed"); break;
            case "replace-race": store.BeforeDeletionComparison = () => File.Move(source.Location, moved); break;
            case "symlink":
                File.Move(source.Location, moved); File.CreateSymbolicLink(source.Location, moved); break;
            case "hardlink": CreateHardLink(source.Location + ".alias", source.Location, nint.Zero).Should().BeTrue(); break;
            case "case-alias": File.Move(source.Location, source.Location.ToUpperInvariant()); break;
            case "unknown": store = Create(); break;
            case "outside":
                saved = new(source with { Location = Path.Combine(root, "outside.instructions.md") }, saved.Revision, saved.Files); break;
            case "recovery":
                store.AfterComponentPublished = _ => throw new IOException("Unfinished save");
                await ((Func<Task>)(() => Save(store, source, saved, Primary(source, "changed")))).Should().ThrowAsync<IOException>();
                break;
        }
        var exists = File.Exists(source.Location);
        var before = exists ? File.ReadAllBytes(source.Location) : [];
        var operation = () => store.Delete(saved, () => current, action => { action(); return Task.CompletedTask; }, cancellation.Token);
        await operation.Should().ThrowAsync<Exception>();
        File.Exists(source.Location).Should().Be(exists);
        if (exists) { File.ReadAllBytes(source.Location).Should().Equal(before); }
        Directory.Exists(Path.GetDirectoryName(source.Location)).Should().BeTrue();
        if (File.Exists(moved)) { File.ReadAllBytes(moved).Should().Equal(originalBytes); }
    }

    [Theory]
    [InlineData("readback")]
    [InlineData("replacement")]
    [InlineData("privacy")]
    [InlineData("cancel")]
    public async Task CommittedSharedDeletionReportsUncertaintyWithoutRetryOrRollback(string failure)
    {
        var store = Create();
        var source = await store.Create(CustomizationKind.Prompt, CustomizationScope.Kora, "example", TestContext.Current.CancellationToken);
        var saved = await Save(store, source, null, Primary(source));
        var current = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        store.AfterDeletionCommitted = () =>
        {
            if (failure is "readback") { throw new IOException("Readback unavailable"); }
            if (failure is "replacement") { File.WriteAllBytes(source.Location, Encoding.UTF8.GetBytes("New unrelated bytes")); }
            if (failure is "privacy") { current = false; }
            if (failure is "cancel") { cancellation.Cancel(); }
        };
        var operation = () => store.Delete(saved, () => current, action => { action(); return Task.CompletedTask; }, cancellation.Token);
        await operation.Should().ThrowAsync<CustomizationDeletionUnconfirmedException>();
        File.Exists(source.Location).Should().Be(failure is "replacement");
        if (failure is "replacement") { File.ReadAllText(source.Location).Should().Be("New unrelated bytes"); }
        Directory.Exists(Path.GetDirectoryName(source.Location)).Should().BeTrue();
    }
}
