using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Windows.Artifacts;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed partial class WindowsOwnedInstructionStoreTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction, false)]
    [InlineData(ArtifactKind.Instruction, true)]
    [InlineData(ArtifactKind.Prompt, false)]
    [InlineData(ArtifactKind.Prompt, true)]
    public async Task NewCopyNeverReplacesAnExistingOrConcurrentlyCreatedDestination(ArtifactKind kind, bool concurrent)
    {
        var store = Create(kind);
        var source = await store.Write("example", null, Document(bom: true), () => true, TestContext.Current.CancellationToken);
        var copy = source.Current!.Document.WithOwnedCopyName("separate");
        var destination = Path.Combine(OwnedDirectory, "separate" + OwnedDefinitionKind.Extension(kind));
        var occupied = copy.Text + "Occupied by another writer.";
        if (concurrent)
        { store.AfterFinalComparison = () => WriteOwnedFile(destination, ArtifactFileDocument.Encode(occupied, true)); }
        else { WriteOwnedFile(destination, ArtifactFileDocument.Encode(occupied, true)); }
        if (concurrent)
        {
            var write = () => store.Write("separate", null, copy, () => true, TestContext.Current.CancellationToken);
            (await write.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("another new ID");
        }
        else
        {
            var result = await store.Write("separate", null, copy, () => true, TestContext.Current.CancellationToken);
            result.Conflict.Should().BeTrue();
            result.Published.Should().BeFalse();
        }
        File.ReadAllBytes(destination).Should().Equal(ArtifactFileDocument.Encode(occupied, true));
        File.ReadAllBytes(Target).Should().Equal(source.Current.Document.CopyBytes());
        Directory.GetFiles(OwnedDirectory, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task RepeatedCopiesFillOnlyTheirOwnInventoryWithoutEvictingOriginalOrOtherKind(ArtifactKind kind)
    {
        var store = Create(kind);
        var source = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        for (var index = 1; index < 128; index++)
        {
            var name = "copy-" + index;
            var result = await store.Write(name, null, source.Current!.Document.WithOwnedCopyName(name), () => true, TestContext.Current.CancellationToken);
            result.Published.Should().BeTrue();
        }
        var rejected = () => store.Write("overflow", null, source.Current!.Document.WithOwnedCopyName("overflow"), () => true, TestContext.Current.CancellationToken);
        await rejected.Should().ThrowAsync<InvalidDataException>();
        (await store.List(TestContext.Current.CancellationToken)).Should().HaveCount(128);
        File.ReadAllBytes(Target).Should().Equal(source.Current!.Document.CopyBytes());
        File.Exists(Path.Combine(OwnedDirectory, "overflow" + OwnedDefinitionKind.Extension(kind))).Should().BeFalse();
        var otherKind = kind == ArtifactKind.Instruction ? ArtifactKind.Prompt : ArtifactKind.Instruction;
        var other = new WindowsOwnedInstructionStore(new Paths(root), otherKind);
        (await other.List(TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction, "Separate")]
    [InlineData(ArtifactKind.Prompt, "separate")]
    public async Task CopyRejectsCaseAliasOrHardlinkedDestinationWithoutChangingOriginal(ArtifactKind kind, string name)
    {
        var store = Create(kind);
        var source = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        var destination = Path.Combine(OwnedDirectory, name + OwnedDefinitionKind.Extension(kind));
        if (name is "Separate") { WriteOwnedFile(destination, Document("Case alias").CopyBytes()); }
        else { CreateHardLink(destination, Target, nint.Zero).Should().BeTrue(); }
        var operation = () => store.Write("separate", null, source.Current!.Document.WithOwnedCopyName("separate"), () => true, TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(Target).Should().Equal(source.Current!.Document.CopyBytes());
        File.Exists(destination).Should().BeTrue();
    }

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task CopyReadbackFailureRetainsPublishedCopyAndOriginalWithNoRollback(ArtifactKind kind)
    {
        var store = Create(kind);
        var source = await store.Write("example", null, Document(), () => true, TestContext.Current.CancellationToken);
        var copy = source.Current!.Document.WithOwnedCopyName("separate");
        using var cancellation = new CancellationTokenSource();
        store.AfterPublication = () => cancellation.Cancel();
        var operation = () => store.Write("separate", null, copy, () => true, cancellation.Token);
        await operation.Should().ThrowAsync<InstructionPublicationUnconfirmedException>();
        File.ReadAllBytes(Path.Combine(OwnedDirectory, "separate" + OwnedDefinitionKind.Extension(kind))).Should().Equal(copy.CopyBytes());
        File.ReadAllBytes(Target).Should().Equal(source.Current.Document.CopyBytes());
    }
}
