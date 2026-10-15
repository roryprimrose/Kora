using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public async Task AdmittedMetadataObservationAndFiltersDoNotReadBodiesWriteOrActivateAndExactInspectionUsesTheOriginalRow(ArtifactKind kind)
    {
        await using var f = new Fixture(kind);
        var original = f.Files.Current!;
        var savedState = f.State.ReadText(kind == ArtifactKind.Instruction ? "instruction-revisions.json" : "prompt-revisions.json");
        var names = await f.Actions.List(f.Eligible, CancellationToken.None);
        var snapshot = await f.Actions.ObserveInventory(names, f.Eligible, CancellationToken.None);
        snapshot.Rows.Should().ContainSingle();
        snapshot.Page("example", OwnedDefinitionUseFilter.ReviewRequired).Rows.Should().ContainSingle();
        snapshot.Page("missing", OwnedDefinitionUseFilter.All).Rows.Should().BeEmpty();
        snapshot.Page("example", OwnedDefinitionUseFilter.ReviewRequired, digest: original.Document.Definition.Digest).Rows.Should().ContainSingle();
        snapshot.Page("", OwnedDefinitionUseFilter.UseEnabled, digest: original.Document.Definition.Digest).Rows.Should().BeEmpty();
        snapshot.Page("", OwnedDefinitionUseFilter.All, digest: new string('f', 64)).Rows.Should().BeEmpty();
        f.Actions.IsInventoryCurrent(snapshot).Should().BeTrue();
        f.Files.Reads.Should().Be(0);
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        f.State.ReadText(kind == ArtifactKind.Instruction ? "instruction-revisions.json" : "prompt-revisions.json").Should().Be(savedState);
        var loaded = await f.Actions.InspectInventory(snapshot, snapshot.Rows[0], f.Eligible, CancellationToken.None);
        loaded.Should().BeSameAs(original);
        f.Files.Reads.Should().Be(1);
        f.Control.LastRequest!.SessionId.Should().NotBeNull();
    }

    [Theory]
    [InlineData("replaced")]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("kind")]
    [InlineData("filename")]
    [InlineData("artifact")]
    [InlineData("host")]
    [InlineData("use-state")]
    [InlineData("reload-failed")]
    [InlineData("same-byte-catalogue")]
    public async Task OriginalRowCannotNavigateToAChangedLiveSourceOrCatalogue(string change)
    {
        await using var f = new Fixture();
        var snapshot = await f.Actions.ObserveInventory(["example"], f.Eligible, CancellationToken.None);
        var exact = snapshot.Page("", OwnedDefinitionUseFilter.All, digest: f.Files.Current!.Document.Definition.Digest);
        exact.Rows.Should().ContainSingle();
        f.Files.BeforeRead = () =>
        {
            var original = f.Files.Current!;
            if (change is "replaced") { f.Files.Current = original with { FileIdentity = new string('c', 48) }; }
            if (change is "changed") { f.Files.Current = Snapshot("Changed"); }
            if (change is "missing") { f.Files.Current = null; }
            if (change is "kind") { f.Files.Current = Snapshot(kind: ArtifactKind.Prompt); }
            if (change is "filename") { f.Files.Current = original with { CanonicalFileName = "Example.instructions.md" }; }
            if (change is "artifact")
            {
                f.Files.Current = new("example", original.DirectoryIdentity, original.FileIdentity,
                    ArtifactFileDocument.Parse(original.Document.CopyBytes(), ArtifactKind.Instruction, "other-source", "example"));
            }
            if (change is "host") { f.Open = false; }
            if (change is "use-state") { f.Catalogue.Update(items => items.Select(item => item with { IsUseEnabled = true }).ToArray()); }
            if (change is "reload-failed") { f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); }
            if (change is "same-byte-catalogue") { f.Catalogue.Publish(f.Catalogue.Candidates); }
        };
        var inspect = () => f.Actions.InspectInventory(snapshot, exact.Rows[0], f.Eligible, CancellationToken.None);
        await inspect.Should().ThrowAsync<InvalidOperationException>();
        f.Files.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("failure-recovered")]
    [InlineData("publication")]
    [InlineData("foreign-kind")]
    [InlineData("foreign-row")]
    [InlineData("unmapped")]
    [InlineData("unrevisioned")]
    [InlineData("host")]
    public async Task UnavailableForeignOrUnmappedObservationNeverProvesAMatch(string reason)
    {
        await using var f = new Fixture();
        var snapshot = await f.Actions.ObserveInventory(["example"], f.Eligible, CancellationToken.None);
        var row = snapshot.Rows[0];
        if (reason is "failure" or "failure-recovered") { f.Catalogue.ReportReloadFailure(new IOException("Unavailable")); }
        if (reason is "failure-recovered" or "publication") { f.Catalogue.Publish(f.Catalogue.Candidates); }
        if (reason is "foreign-kind") { snapshot = new(ArtifactKind.Prompt, ["example"], f.Catalogue.Candidates, 0, DateTimeOffset.UtcNow); }
        if (reason is "foreign-row")
        { row = (await f.Actions.ObserveInventory(["example"], f.Eligible, CancellationToken.None)).Rows[0]; }
        if (reason is "unmapped")
        { snapshot = await f.Actions.ObserveInventory(["unmapped"], f.Eligible, CancellationToken.None); row = snapshot.Rows[0]; }
        if (reason is "unrevisioned")
        {
            f.Catalogue.Publish([Candidate(f.Files.Current!) with { OwnedInstructionRevision = null, IsUseEnabled = true }]);
            snapshot = await f.Actions.ObserveInventory(["example"], f.Eligible, CancellationToken.None); row = snapshot.Rows[0];
        }
        if (reason is "host") { f.Open = false; }
        var inspect = () => f.Actions.InspectInventory(snapshot, row, f.Eligible, CancellationToken.None);
        await inspect.Should().ThrowAsync<InvalidOperationException>();
        f.Files.Reads.Should().Be(0);
    }

    [Fact]
    public async Task FailedReloadCannotPublishFreshObservationAndCurrentUseChangeNotifiesThenInvalidatesRows()
    {
        await using var f = new Fixture();
        var changed = 0;
        var failed = 0;
        void OnChanged(object? sender, EventArgs args) => changed++;
        void OnFailed(object? sender, ArtifactCatalogueReloadFailedEventArgs args) => failed++;
        f.Actions.InventoryChanged += OnChanged;
        f.Actions.InventoryReloadFailed += OnFailed;
        var snapshot = await f.Actions.ObserveInventory(["example"], f.Eligible, CancellationToken.None);
        f.Catalogue.ReportReloadFailure(new IOException("Unavailable"));
        f.Actions.IsInventoryCurrent(snapshot).Should().BeFalse();
        var observe = () => f.Actions.ObserveInventory(["example"], f.Eligible, CancellationToken.None);
        await observe.Should().ThrowAsync<InvalidOperationException>();
        failed.Should().Be(1);
        f.Catalogue.Update(items => items.Select(item => item with { IsUseEnabled = true }).ToArray());
        f.Catalogue.IsReloadUnavailable.Should().BeTrue();
        f.Catalogue.Publish(f.Catalogue.Candidates);
        var fresh = await f.Actions.ObserveInventory(["example"], f.Eligible, CancellationToken.None);
        fresh.Rows[0].ObservedUseEnabled.Should().BeTrue();
        f.Actions.IsInventoryCurrent(fresh).Should().BeTrue();
        changed.Should().Be(1);
        f.Actions.InventoryChanged -= OnChanged;
        f.Actions.InventoryReloadFailed -= OnFailed;
        f.Catalogue.Update(items => items.Select(item => item with { IsUseEnabled = false }).ToArray());
        f.Catalogue.ReportReloadFailure(new IOException("Unavailable"));
        changed.Should().Be(1);
        failed.Should().Be(1);
    }
}
