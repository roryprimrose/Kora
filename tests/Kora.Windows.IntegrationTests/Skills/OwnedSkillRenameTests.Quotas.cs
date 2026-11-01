using System.Globalization;
using AwesomeAssertions;
using Kora.Core.Skills;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Skills;

public sealed partial class OwnedSkillRenameTests
{
    [Theory]
    [InlineData("rename-intent-durable")]
    [InlineData("rename-destination-verified")]
    [InlineData("rename-component-retired")]
    [InlineData("rename-source-retired")]
    public async Task PendingRenameReservesOneImmutableVersionThroughUnrelatedPublicationRestartAndRecovery(string phase)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var original = await Save(store, Document());
        var proposal = new OwnedSkillRename(original, "renamed");
        store.Checkpoint = point => { if (string.Equals(point, phase, StringComparison.Ordinal)) { throw new IOException("injected boundary"); } };
        await store.Invoking(value => Rename(value, proposal)).Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
        var restarted = new WindowsOwnedDeclarativeSkillStore(paths);
        var pending = (await restarted.PendingRenames(Token)).Single();
        for (var index = 1; index < SharedSkillCatalogue.MaximumPackages; index++)
        { await Save(restarted, Document(index % 2 == 0 ? "other-even" : "other-odd", index.ToString(CultureInfo.InvariantCulture) + ".0.0")); }
        var excess = Document("other-even", "100.0.0");
        await restarted.Invoking(value => Save(value, excess)).Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(restarted.Destination(excess)).Should().BeFalse();
        (await restarted.List(Token)).Should().HaveCount(SharedSkillCatalogue.MaximumPackages - 1);
        var recovered = await Rename(restarted, pending, recovery: true);
        (await restarted.List(Token)).Should().HaveCount(SharedSkillCatalogue.MaximumPackages);
        (await restarted.PendingRenames(Token)).Should().BeEmpty();
        (await restarted.Read("example", "1.0.0", Token)).Should().BeNull();
        (await restarted.Read("renamed", "1.0.0", Token))!.IsSameRevision(recovered).Should().BeTrue();
        var renamedAgain = await Rename(restarted, new(recovered, "renamed-again"));
        renamedAgain.Document.IsAvailableForInvocation.Should().BeFalse();
        (await restarted.List(Token)).Should().HaveCount(SharedSkillCatalogue.MaximumPackages);
    }

    [Theory]
    [InlineData("rename-intent-durable", false)]
    [InlineData("rename-destination-verified", false)]
    [InlineData("rename-component-retired", false)]
    [InlineData("rename-source-retired", false)]
    [InlineData("rename-intent-durable", true)]
    [InlineData("rename-destination-verified", true)]
    [InlineData("rename-component-retired", true)]
    [InlineData("rename-source-retired", true)]
    public async Task PendingRenameReservesCompleteLargerPackageBytesAndExactLimitRecoveryDoesNotBreakList(string phase, bool shorten)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var original = await Save(store, Document(shorten ? "long-original-name" : "example"));
        var proposal = new OwnedSkillRename(original, shorten ? "new" : "renamed-longer");
        store.Checkpoint = point => { if (string.Equals(point, phase, StringComparison.Ordinal)) { throw new IOException("injected boundary"); } };
        await store.Invoking(value => Rename(value, proposal)).Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
        var restarted = new WindowsOwnedDeclarativeSkillStore(paths);
        var destinationBytes = proposal.Renamed.ManifestBytes.Length + proposal.Renamed.InstructionBytes.Length;
        var reservationBytes = Math.Max(original.Document.ManifestBytes.Length + original.Document.InstructionBytes.Length, destinationBytes);
        var remaining = SharedSkillCatalogue.MaximumTotalBytes - reservationBytes;
        for (var index = 1; remaining > 0; index++)
        {
            var bytes = Math.Min(SharedSkillSnapshot.MaximumBytes, remaining);
            await Save(restarted, SizedDocument("other", index.ToString(CultureInfo.InvariantCulture) + ".0.0", bytes));
            remaining -= bytes;
        }
        var excess = Document("excess");
        await restarted.Invoking(value => Save(value, excess)).Should().ThrowAsync<InvalidDataException>();
        Directory.Exists(restarted.Destination(excess)).Should().BeFalse();
        var visible = await restarted.List(Token);
        visible.Sum(value => value.Document.ManifestBytes.Length + value.Document.InstructionBytes.Length)
            .Should().Be(SharedSkillCatalogue.MaximumTotalBytes - reservationBytes);
        var recovered = await Rename(restarted, (await restarted.PendingRenames(Token)).Single(), recovery: true);
        var catalogue = await restarted.List(Token);
        catalogue.Sum(value => value.Document.ManifestBytes.Length + value.Document.InstructionBytes.Length)
            .Should().Be(SharedSkillCatalogue.MaximumTotalBytes - reservationBytes + destinationBytes);
        (await restarted.PendingRenames(Token)).Should().BeEmpty();
        (await restarted.Read(proposal.Renamed.Id, "1.0.0", Token))!.IsSameRevision(recovered).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RecoveryOfAnAlreadyOverLimitInventoryRetainsObservedPackagesAndIntentInsteadOfClaimingSuccess(bool bytes, bool sourceRetired)
    {
        using var paths = new OwnedStorageFixture();
        var store = new WindowsOwnedDeclarativeSkillStore(paths);
        var original = await Save(store, Document());
        var proposal = new OwnedSkillRename(original, "renamed");
        store.Checkpoint = point =>
        {
            if (string.Equals(point, sourceRetired ? "rename-source-retired" : "rename-destination-verified", StringComparison.Ordinal))
            { throw new IOException("injected boundary"); }
        };
        await store.Invoking(value => Rename(value, proposal)).Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
        var directory = RestrictedStorageDirectory.ForOwnedSkills(paths);
        var idPath = Path.Combine(directory.Root, "other");
        directory.CreateOwnedSkillDirectory(idPath);
        var count = bytes ? SharedSkillCatalogue.MaximumTotalBytes / SharedSkillSnapshot.MaximumBytes : SharedSkillCatalogue.MaximumPackages;
        for (var index = 1; index <= count; index++)
        {
            var version = index.ToString(CultureInfo.InvariantCulture) + ".0.0";
            var document = bytes ? SizedDocument("other", version, SharedSkillSnapshot.MaximumBytes) : Document("other", version);
            var package = store.Destination(document);
            directory.CreateOwnedSkillDirectory(package);
            using (var manifest = directory.CreateNewFile(Path.Combine(package, "manifest.yaml")))
            { manifest.Write(document.ManifestBytes.Span); manifest.Flush(true); }
            using (var instructions = directory.CreateNewFile(Path.Combine(package, "instructions.md")))
            { instructions.Write(document.InstructionBytes.Span); instructions.Flush(true); }
        }
        var restarted = new WindowsOwnedDeclarativeSkillStore(paths);
        var pending = (await restarted.PendingRenames(Token)).Single();
        var failure = await restarted.Invoking(value => Rename(value, pending, recovery: true)).Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>();
        failure.Which.Effect.Should().Be(OwnedSkillRenameEffect.SourceRetirementStarted);
        failure.Which.InnerException.Should().BeOfType<InvalidDataException>();
        (await restarted.PendingRenames(Token)).Should().ContainSingle();
        await restarted.Invoking(value => value.List(Token)).Should().ThrowAsync<InvalidDataException>();
        await restarted.Invoking(value => value.Read("example", "1.0.0", Token)).Should().ThrowAsync<InvalidOperationException>();
        await restarted.Invoking(value => value.Read("renamed", "1.0.0", Token)).Should().ThrowAsync<InvalidOperationException>();
        Directory.Exists(store.Destination(original.Document)).Should().Be(!sourceRetired);
        foreach (var document in sourceRetired ? new[] { proposal.Renamed } : new[] { original.Document, proposal.Renamed })
        {
            File.ReadAllBytes(Path.Combine(store.Destination(document), "manifest.yaml")).Should().Equal(document.ManifestBytes.ToArray());
            File.ReadAllBytes(Path.Combine(store.Destination(document), "instructions.md")).Should().Equal(document.InstructionBytes.ToArray());
        }
    }

    private static OwnedDeclarativeSkillDocument SizedDocument(string id, string version, int bytes)
    {
        var document = Document(id, version);
        return OwnedDeclarativeSkillDocument.Create(id, version, document.Name, document.Description,
            new string('x', bytes - document.ManifestBytes.Length));
    }
}
