using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task PreparationNavigatesExactActiveQueueFromDoneOnlyFilterWithoutLatestSelectionFallback()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var h = new QueuePreparationHarness(f);
        await h.InitializeAsync();
        var entries = h.Model.QueueRecords.ToArray();
        h.Model.SessionFilter = SessionListFilter.Done;
        h.Model.SelectedSessionRecord.Should().BeNull();
        var preparation = await h.PrepareAsync(entries);
        (await h.Model.PrepareQueueCancellationAsync(preparation, () => true, f.Token)).Should().BeTrue();
        h.Model.SessionFilter.Should().Be(SessionListFilter.All);
        h.Model.Sessions.Should().ContainSingle().Which.Authority.SessionId.Should().Be(f.Request.SessionId);
        h.Model.CanConfirmPendingCancellation.Should().BeTrue();
        h.Action.Reads.Should().Be(0);
    }

    [Fact]
    public async Task PreparationUsesSameNativeSelectorAndCompleteReviewWithoutChangingIndependentWorkSelection()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var h = new QueuePreparationHarness(f);
        await h.InitializeAsync();
        var model = h.Model;
        var original = model.QueueRecords.ToArray();
        model.SelectWorkRecord(model.WorkRecords.Single(row => row.TaskId == original[1].Request.TaskId.Value));
        var selectedWork = model.SelectedWorkRecord;
        model.NameDraft = "unrelated name draft";
        var prepared = await h.PrepareAsync([original[1], original[0]]);
        var tasks = f.Count("host_tasks");
        var audits = f.Count("security_audit_events");
        var source = model.CaptureQueueCancellationInputEligibility();
        (await model.PrepareQueueCancellationAsync(prepared, () => true, f.Token)).Should().BeTrue();
        source().Should().BeFalse("the explicit pending subset changed");
        model.SelectedWorkRecord.Should().BeSameAs(selectedWork);
        model.NameDraft.Should().Be("unrelated name draft");
        model.CanConfirmPendingCancellation.Should().BeTrue();
        model.PendingCancellationStatus.Should().Contain("subset: 2").And.Contain("prerequisite None")
            .And.Contain("queue revision").And.Contain("run ").And.Contain("paused False").And.Contain("held False");
        foreach (var entry in original)
        {
            model.PendingCancellationStatus.Should().Contain(entry.Request.TaskId.Value.ToString("D"))
                .And.Contain(entry.ExpiresAt.ToString("O"));
        }
        model.PendingCancellationStatus.IndexOf(original[0].Request.TaskId.Value.ToString("D"), StringComparison.Ordinal)
            .Should().BeLessThan(model.PendingCancellationStatus.IndexOf(original[1].Request.TaskId.Value.ToString("D"), StringComparison.Ordinal));
        f.Count("host_tasks").Should().Be(tasks);
        f.Count("security_audit_events").Should().Be(audits);
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().Equal(original);
        await model.DiscardPendingCancellationAsync();
        model.PendingCancellationStatus.Should().Contain("Selected 2");
        model.CanConfirmPendingCancellation.Should().BeFalse();
        await model.ReviewPendingCancellationAsync();
        model.CanConfirmPendingCancellation.Should().BeTrue("the existing native review remains reusable after discard");
        h.Action.Reads.Should().Be(0);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("save")]
    [InlineData("discard")]
    [InlineData("unconfirmed")]
    [InlineData("revoked")]
    [InlineData("control")]
    [InlineData("closed")]
    public async Task PreparationUsesOriginalDirtyComposerAdmissionAndNeverCommitsOrRetriesOnUncertainty(string choice)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var h = new QueuePreparationHarness(f);
        await h.InitializeAsync();
        var model = h.Model;
        await model.ReadConversationAsync();
        model.ComposerBody = "original private unsaved buffer";
        var original = model.QueueRecords.ToArray();
        var prepared = await h.PrepareAsync(original);
        var admitted = true;
        model.DecideComposerDraft = () =>
        {
            if (choice is "unconfirmed") { throw new IOException("unconfirmed original save"); }
            if (choice is "revoked") { admitted = false; }
            if (choice is "control") { h.Access.ControlRevision++; }
            if (choice is "closed") { model.Close(); }
            return Task.FromResult(choice is "discard" ? ComposerDraftDecision.Discard : ComposerDraftDecision.Cancel);
        };
        var tasks = f.Count("host_tasks");
        (await model.PrepareQueueCancellationAsync(prepared, () => admitted, f.Token)).Should().BeTrue();
        model.CanConfirmPendingCancellation.Should().BeTrue();
        if (choice is not "closed")
        { model.ComposerBody.Should().Be("original private unsaved buffer"); }
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().Equal(original);
        (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Body
            .Should().BeNull();
        f.Count("host_tasks").Should().Be(tasks, "preparation preserves unsent input without saving");
        f.Count("conversation_messages").Should().Be(0);
        h.Action.Reads.Should().Be(0);
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("pause")]
    [InlineData("task")]
    [InlineData("control")]
    [InlineData("admission")]
    [InlineData("token")]
    public async Task FrozenPreparationNeverSelectsRemainingOrLatestSourceAfterChange(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var h = new QueuePreparationHarness(f);
        await h.InitializeAsync();
        var original = h.Model.QueueRecords.ToArray();
        var preparation = await h.PrepareAsync(original);
        var snapshot = preparation.Work.Queue;
        if (change is "queue")
        {
            h.Model.ChooseNoQueuePrerequisite();
            await h.Model.EnqueueVersionAsync();
        }
        if (change is "pause") { await h.Model.PauseQueueAsync(); }
        if (change is "task")
        {
            h.Model.SelectQueueEntry(original[1]);
            await h.Model.CancelQueueEntryAsync();
        }
        if (change is "control") { h.Access.ControlRevision++; }
        using var cancel = new CancellationTokenSource();
        if (change is "token") { cancel.Cancel(); }
        var current = (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.ToArray();
        var count = f.Count("host_tasks");
        (await h.Model.PrepareQueueCancellationAsync(preparation, () => change is not "admission", cancel.Token)).Should().BeFalse();
        h.Model.CanConfirmPendingCancellation.Should().BeFalse();
        h.Model.Status.Should().ContainAny("cancelled", "unconfirmed");
        f.Count("host_tasks").Should().Be(count);
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().Equal(current);
        snapshot.Entries.Should().Equal(original);
        h.Action.Reads.Should().Be(0);
    }

    [Fact]
    public async Task BufferAndNativeAdmissionAreExactLifetimesAndCannotConfirmAfterRevocation()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var h = new QueuePreparationHarness(f);
        await h.InitializeAsync();
        var model = h.Model;
        var source = model.CaptureQueueCancellationInputEligibility();
        source().Should().BeTrue();
        model.ComposerBody = "edited";
        model.ComposerBody = "";
        source().Should().BeFalse("a changed-and-restored input is not the original lifetime");
        var native = true;
        var prepared = await h.PrepareAsync(model.QueueRecords);
        (await model.PrepareQueueCancellationAsync(prepared, () => native, f.Token)).Should().BeTrue();
        var before = f.Count("host_tasks");
        native = false;
        model.CanConfirmPendingCancellation.Should().BeFalse();
        await model.ConfirmPendingCancellationAsync();
        f.Count("host_tasks").Should().Be(before);
        model.QueueRecords.Should().BeEmpty("failed confirmation revokes uncertain local presentation, not storage");
        (await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token)).Entries.Should().HaveCount(2);
        h.Action.Reads.Should().Be(0);
    }

    private sealed class QueuePreparationHarness : IAsyncDisposable
    {
        private readonly InteractionStorageFixture fixture;
        private readonly Composition conversation;
        private readonly SessionQueueService queue;
        internal WindowsSqliteSessionWorkspaceTests.Access Access => conversation.Access;
        internal PreparationVersionAction Action { get; } = new();
        internal SessionsViewModel Model { get; }
        internal SessionWorkspaceService Workspace { get; }
        internal QueuePreparationHarness(InteractionStorageFixture fixture)
        {
            this.fixture = fixture;
            conversation = new(fixture);
            queue = new(fixture.Store, fixture.Store, new(fixture.Tasks), Access, Action,
                new(), NullLogger<SessionQueueService>.Instance, fixture.Time);
            Workspace = new(fixture.Store, new(fixture.Tasks), Access, NullLogger<SessionWorkspaceService>.Instance, queue);
            var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
            sink.Initialize();
            Model = new(Workspace, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), Access, fixture.Time,
                NullLogger<DurableEvidenceQuery>.Instance), Access, NullLogger<SessionsViewModel>.Instance, conversation: conversation.Conversation);
        }
        internal async Task InitializeAsync()
        {
            await Model.RefreshAsync();
            await Model.SelectAsync(Model.Sessions.Single());
            await Model.EnqueueVersionAsync();
            Model.ChooseNoQueuePrerequisite();
            await Model.EnqueueCapabilitiesAsync();
        }
        internal Task<SessionQueueCancellationPreparation> PrepareAsync(IReadOnlyList<SessionQueueEntry> entries) =>
            Workspace.PrepareQueueCancellationAsync(new(fixture.Request.SessionId.Value,
                [.. entries.Select(entry => entry.Request.TaskId.Value)]), RequestOrigin.LocalUi, () => true, fixture.Token);
        public async ValueTask DisposeAsync()
        {
            Model.Close();
            await queue.DisposeAsync();
            await conversation.DisposeAsync();
        }
    }
    private sealed class PreparationVersionAction : IDeterministicVersionQueueAction
    {
        internal int Reads { get; private set; }
        public CapabilityReply Observe(SessionQueueReadProfile profile, CancellationToken token, SessionQueueCapabilityTarget? target = null)
        {
            Reads++;
            return new(CapabilityOutcome.Succeeded, "observed", Version: new("1.0.0", "Fixed local read."));
        }
    }
}
