using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("cancel")]
    [InlineData("discard")]
    [InlineData("save")]
    [InlineData("decision-failure")]
    [InlineData("unconfirmed-save")]
    [InlineData("input")]
    [InlineData("native")]
    [InlineData("buffer")]
    [InlineData("control")]
    [InlineData("queue")]
    [InlineData("entry")]
    public async Task ReplacementPreparationGuardsOriginalDirtyBufferAndNeverConfirmsOrChangesUnrelatedSelection(string decision)
    {
        using var f = new InteractionStorageFixture();
        var checkpoint = new InteractionTransactionCheckpoint();
        f.Reopen(checkpoint);
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var conversation = new Composition(f);
        var action = new ReplacementNeverAction();
        await using var queue = new SessionQueueService(f.Store, f.Store, new(f.Tasks), conversation.Access, action,
            new(), NullLogger<SessionQueueService>.Instance, f.Time);
        var service = new SessionWorkspaceService(f.Store, new(f.Tasks), conversation.Access,
            NullLogger<SessionWorkspaceService>.Instance, queue);
        var sink = new WindowsSqliteEvidenceSink(f.Paths);
        sink.Initialize();
        var model = new SessionsViewModel(service, new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink),
            conversation.Access, f.Time, NullLogger<DurableEvidenceQuery>.Instance), conversation.Access,
            NullLogger<SessionsViewModel>.Instance, conversation: conversation.Conversation);
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ChooseNoQueuePrerequisite();
        await model.EnqueueVersionAsync();
        var entry = model.QueueRecords.Single();
        f.Time.Now = entry.ExpiresAt;
        await model.ReadConversationAsync();
        model.ComposerBody = "original private dirty buffer";
        var original = model.SelectedSessionRecord;
        var originalWork = model.WorkRecords.Single(row => row.TaskId == entry.Request.TaskId.Value);
        model.SelectWorkRecord(originalWork);
        var held = new TaskCompletionSource<ComposerDraftDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.DecideComposerDraft = () =>
        {
            reached.SetResult();
            return decision is "decision-failure" ? throw new IOException("private save failed") : held.Task;
        };
        var admitted = true;
        var native = true;
        var count = f.Count("host_tasks");
        var review = model.PrepareExactReplacementReviewAsync(new(f.Request.SessionId.Value, entry.Request.TaskId.Value),
            RequestOrigin.ActivatedVoice, () => admitted, () => native, f.Token);
        await reached.Task;
        switch (decision)
        {
            case "input": admitted = false; break;
            case "native": native = false; break;
            case "buffer":
                model.ComposerBody = "edited private buffer";
                model.ComposerBody.Should().Be("original private dirty buffer", "the native decision makes this buffer read-only");
                break;
            case "control": conversation.Access.ControlRevision++; break;
            case "queue":
                var observed = await f.Store.ReadQueueAsync(f.Request.SessionId, f.Token);
                await queue.ExecuteCommandAsync(new(SessionCommandOperation.QueuePause, f.Request.SessionId.Value, 1)
                {
                    QueueRevision = observed.Revision, QueueRunId = observed.DispatchControl!.RunId,
                    QueueControlRevision = observed.DispatchControl.Revision,
                }, RequestOrigin.LocalUi, () => true, f.Token);
                break;
            case "entry":
                using (var raw = f.OpenRaw())
                using (var command = raw.CreateCommand())
                {
                    command.CommandText = "UPDATE session_queue SET revision=revision+1;";
                    command.ExecuteNonQuery();
                }
                break;
            case "unconfirmed-save":
                checkpoint.Commit = (_, _) => throw new IOException("synthetic unconfirmed save");
                break;
        }
        held.TrySetResult(decision is "save" or "unconfirmed-save" ? ComposerDraftDecision.Save
            : decision is "cancel" ? ComposerDraftDecision.Cancel : ComposerDraftDecision.Discard);
        var shown = false;
        var error = await Record.ExceptionAsync(async () => shown = await review);
        var accepted = decision is "discard" or "save" or "buffer";
        shown.Should().Be(accepted);
        model.CanConfirmExpiredRequeue.Should().Be(accepted);
        if (!accepted)
        {
            model.SelectedSessionRecord.Should().Be(original);
            model.SelectedWorkRecord.Should().BeSameAs(originalWork);
        }
        if (decision is "cancel" or "decision-failure" or "unconfirmed-save" or "input" or "native" or "control")
        { model.ComposerBody.Should().Be("original private dirty buffer"); }
        if (decision is "save")
        {
            error.Should().BeNull();
            model.ComposerDirty.Should().BeFalse();
            (await f.Store.ReadConversationDraftAsync(f.Request.SessionId, f.Token)).Draft.Body.Should().Be("original private dirty buffer");
        }
        f.Count("host_tasks").Should().Be(count);
        f.Count("conversation_messages").Should().Be(0);
        if (decision is not "entry")
        {
            (await f.Store.ReadQueueEntryAsync(f.Request.SessionId, entry.Request.TaskId, f.Token))!.State
                .Should().Be(SessionQueueState.Pending);
        }
        action.Reads.Should().Be(0);
        model.Close();
    }

    private sealed class ReplacementNeverAction : IDeterministicVersionQueueAction
    {
        internal int Reads { get; private set; }
        public CapabilityReply Observe(SessionQueueReadProfile profile, CancellationToken token,
            SessionQueueCapabilityTarget? target = null)
        {
            Reads++;
            throw new InvalidOperationException("Preparation never dispatches.");
        }
    }
}
