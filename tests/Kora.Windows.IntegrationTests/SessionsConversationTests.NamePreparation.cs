using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    private static SessionNamePreparationCommand PrepareNameCommand(Guid id, string name = "Café \"new\" 東京") =>
        SessionNamePreparationCommand.Parse("session name prepare " + id.ToString("D") + " \""
            + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"", "Kora")!;

    [Fact]
    public async Task PreparedRenameSourceFenceIsCheckedInsideTheOriginalMutationTransaction()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var observed = await composed.Workspace.ReadNamePreparationAsync(f.Request.SessionId, f.Token);
        var other = await composed.Workspace.CreateAsync(new("Collision label"), RequestOrigin.LocalUi, f.Token);
        var rename = () => composed.Workspace.RenameAsync(f.Request.SessionId, observed.Entry.Authority.Generation,
            0, new("Collision label"), RequestOrigin.LocalUi, f.Token, () => true, observed.SourceRevision);
        await rename.Should().ThrowAsync<InvalidOperationException>().WithMessage("*source changed*");
        (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token)).Metadata.Should().BeNull();
        (await f.Store.ReadMetadataAsync(other.Authority.SessionId, f.Token)).Should().Be(other);
        (await composed.Model.PrepareNameAsync(PrepareNameCommand(f.Request.SessionId.Value, "Collision label"),
            RequestOrigin.LocalUi, () => true, () => true, f.Token)).Should().BeTrue();
        await composed.Model.RenamePreparedNameAsync();
        (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token)).Metadata!.Name.Value.Should().Be("Collision label");
        (await f.Store.ReadMetadataAsync(other.Authority.SessionId, f.Token)).Should().Be(other);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparedRenameRetainsCanonicalAuditAndCommitAdmissionFailuresVisibly(bool revokeAtCommit)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var fail = false;
        var admitted = true;
        f.Reopen(new NamePreparationCheckpoint(
            () => { if (fail && !revokeAtCommit) { throw new IOException("synthetic required audit failure"); } },
            () => { if (fail && revokeAtCommit) { admitted = false; } }));
        await using var composed = new Composition(f);
        var model = composed.Model;
        model.NameDraft = "independent dirty name";
        (await model.PrepareNameAsync(PrepareNameCommand(f.Request.SessionId.Value),
            RequestOrigin.LocalUi, () => admitted, () => true, f.Token)).Should().BeTrue();
        fail = true;
        await model.RenamePreparedNameAsync();
        model.Status.Should().Contain("unavailable/unconfirmed");
        model.NameDraft.Should().Be("independent dirty name");
        model.HasNamePreparation.Should().BeFalse();
        (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token)).Metadata.Should().BeNull();
    }

    private sealed class NamePreparationCheckpoint(Action audit, Action commit) : IHostInteractionTransactionCheckpoint
    {
        public void BeforeAudit(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction) => audit();
        public void BeforeCommit(Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction) => commit();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task NamePreparationDisclosesCompleteNamesWithoutMutatingSessionOrIndependentEditor(bool active, bool named)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        if (named) { await composed.Workspace.RenameAsync(f.Request.SessionId, new(1), 0, new("Old  Café 東京"), RequestOrigin.LocalUi, f.Token); }
        if (!active) { await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId, new(1), false, RequestOrigin.LocalUi, f.Token); }
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.NameDraft = "independently dirty name";
        var original = model.SelectedSessionRecord!;
        var tasks = f.Count("host_tasks");
        var audits = f.Count("security_audit_events");
        var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
        var displayed = await model.PrepareNameAsync(PrepareNameCommand(f.Request.SessionId.Value),
            RequestOrigin.LocalUi, () => true, () => true, f.Token);
        displayed.Should().BeTrue();
        model.NamePreparationReview.Should().Contain("Café \"new\" 東京")
            .And.Contain(named ? "Old  Café 東京" : "(unnamed; no metadata)")
            .And.Contain(active ? "Active" : "Done").And.Contain(f.Request.SessionId.Value.ToString("D"));
        model.NameDraft.Should().Be("independently dirty name");
        model.SelectedSessionRecord.Should().Be(original);
        f.Count("host_tasks").Should().Be(tasks);
        f.Count("security_audit_events").Should().Be(audits);
        (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token)).Should().Be(original);
        (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
        model.CancelNamePreparation();
        model.NamePreparationReview.Should().BeEmpty();
        model.NameDraft.Should().Be("independently dirty name");
        (await model.PrepareNameAsync(PrepareNameCommand(f.Request.SessionId.Value),
            RequestOrigin.LocalUi, () => true, () => true, f.Token)).Should().BeTrue();
        await model.RenamePreparedNameAsync();
        var renamed = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        renamed.Authority.Should().Be(original.Authority);
        renamed.Metadata!.Name.Value.Should().Be("Café \"new\" 東京");
        model.NameDraft.Should().Be("independently dirty name");
    }

    [Theory]
    [InlineData("Cancel")]
    [InlineData("Discard")]
    [InlineData("Save")]
    [InlineData("Failure")]
    public async Task NamePreparationProtectsComposerAndNeverOverwritesEitherDraft(string decision)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.NameDraft = "original name editor draft";
        model.ComposerBody = "original private unsaved buffer";
        model.DecideComposerDraft = () => decision is "Failure" ? throw new IOException("decision failed")
            : Task.FromResult(Enum.Parse<ComposerDraftDecision>(decision));
        var shown = await model.PrepareNameAsync(PrepareNameCommand(f.Request.SessionId.Value),
            RequestOrigin.LocalUi, () => true, () => true, f.Token);
        shown.Should().Be(decision is "Save" or "Discard");
        model.ComposerBody.Should().Be(decision is "Discard" ? "" : "original private unsaved buffer");
        model.NameDraft.Should().Be("original name editor draft");
        var saved = await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token);
        saved.Draft.Revision.Should().Be(decision is "Save" ? 1 : 0);
        f.Count("conversation_messages").Should().Be(0);
        (await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token)).Metadata.Should().BeNull();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("revision")]
    [InlineData("native")]
    [InlineData("input")]
    [InlineData("editor")]
    [InlineData("composer")]
    [InlineData("selection")]
    [InlineData("source")]
    [InlineData("metadata")]
    [InlineData("generation")]
    [InlineData("replace")]
    public async Task PreparedNameRenameUsesOnlyOriginalCapturedLiteralAndExactRevision(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var composed = new Composition(f);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        var admitted = true;
        var native = true;
        (await model.PrepareNameAsync(PrepareNameCommand(f.Request.SessionId.Value),
            RequestOrigin.ActivatedVoice, () => admitted, () => native, f.Token)).Should().BeTrue();
        switch (change)
        {
            case "cancel": model.CancelNamePreparation(); break;
            case "close": model.Close(); break;
            case "private": composed.Access.CanInspect = false; break;
            case "control": composed.Access.CanControl = false; break;
            case "revision": composed.Access.ControlRevision++; break;
            case "native": native = false; break;
            case "input": admitted = false; break;
            case "editor": model.NameDraft = "independent new name"; break;
            case "composer": model.ComposerBody = "independent new message"; break;
            case "selection": model.HistorySessionId = Guid.NewGuid().ToString("D"); break;
            case "source": model.RevokeSessionList(); break;
            case "metadata": await composed.Workspace.RenameAsync(f.Request.SessionId, new(1), 0,
                new("Other committed name"), RequestOrigin.LocalUi, f.Token); break;
            case "generation": await composed.Workspace.ChangeLifecycleAsync(f.Request.SessionId, new(1), false,
                RequestOrigin.LocalUi, f.Token); break;
            case "replace": await model.PrepareNameAsync(PrepareNameCommand(f.Request.SessionId.Value, "Replacement literal"),
                RequestOrigin.LocalUi, () => true, () => true, f.Token); break;
        }
        var audits = f.Count("security_audit_events");
        await model.RenamePreparedNameAsync();
        var actual = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        actual.Metadata?.Name.Value.Should().Be(change switch
        {
            "none" => "Café \"new\" 東京",
            "replace" => "Replacement literal",
            "metadata" => "Other committed name",
            _ => null,
        });
        if (change is "none" or "replace")
        {
            model.Status.Should().Contain("Committed explicit rename");
            actual.Authority.Generation.Value.Should().Be(1);
            actual.Authority.IsActive.Should().BeTrue();
            var after = f.Count("security_audit_events");
            after.Should().BeGreaterThan(audits);
            await model.RenamePreparedNameAsync();
            f.Count("security_audit_events").Should().Be(after, "the native review is consumed exactly once");
        }
        else { f.Count("security_audit_events").Should().Be(audits); }
        model.HasNamePreparation.Should().BeFalse();
    }
}
