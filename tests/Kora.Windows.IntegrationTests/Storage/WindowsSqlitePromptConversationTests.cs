using AwesomeAssertions;
using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqlitePromptConversationTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("input-at-commit")]
    [InlineData("close-at-commit")]
    [InlineData("owner-at-commit")]
    [InlineData("target-at-commit")]
    [InlineData("audit")]
    [InlineData("late-cancel")]
    [InlineData("credential")]
    public async Task RealPrivateAuthorityKeepsRequiredAuditAndExactOneShotReceiptsAcrossPublicationFailures(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var source = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
            "---\nkora-format: 2\nname: example\ndescription: Test\n---\n${input:topic}", false),
            ArtifactKind.Prompt, "kora-profile", "example").Definition with { OwnedInstructionRevision = "exact-native-source" };
        using var draft = new PromptParameterDraft(source, new([source]), () => true);
        var body = fault is "credential" ? "api_key=synthetic-only-value" : "  immutable e\u0301🙂\r\n${input:topic}  ";
        draft.SetValue("topic", body);
        await draft.Preview(Fresh, f.Token);
        var eligible = true;
        var targetRevision = 1;
        f.Reopen(new Checkpoint(fault, () =>
        {
            if (fault is "input-at-commit") { draft.SetValue("topic", "changed"); }
            if (fault is "close-at-commit") { draft.Dispose(); }
            if (fault is "owner-at-commit") { eligible = false; }
            if (fault is "target-at-commit") { targetRevision++; }
        }));
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(f, access);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var admission = new Admission(() => { if (fault is "late-cancel") { cancellation.Cancel(); } });
        var conversation = new SessionConversationService(f.Store, f.Store, workspace, access, admission, f.Time,
            NullLogger<SessionConversationService>.Instance);
        var workflow = new PromptConversationSubmission(workspace, conversation);
        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        var prepared = await workflow.Prepare(draft, target, Fresh, () => eligible && targetRevision == 1, f.Token);
        var taskCount = f.Count("host_tasks");
        ConversationSubmissionUnconfirmedException? unconfirmed = null;
        if (fault is "success")
        {
            var receipt = await workflow.Submit(prepared, Fresh, () => eligible && targetRevision == 1, cancellation.Token);
            receipt.Message.Body.Should().Be(body);
            receipt.Message.Request.SessionId.Should().Be(target.Authority.SessionId);
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
            receipt.Message.Reason.Should().Be(ModelTurnReason.GenerationNotEnabled);
            receipt.Task.State.Should().Be(HostTaskState.Unavailable);
            f.Count("host_tasks").Should().Be(taskCount + 1);
            admission.Calls.Should().Be(1);
            admission.Context!.UserRequest.Should().Be(body);
        }
        else if (fault is "credential")
        {
            await workflow.Invoking(w => w.Submit(prepared, Fresh, () => true, cancellation.Token))
                .Should().ThrowAsync<ConversationCredentialException>();
            admission.Calls.Should().Be(0);
            f.Count("host_tasks").Should().Be(taskCount + 1);
        }
        else
        {
            unconfirmed = (await workflow.Invoking(w => w.Submit(prepared, Fresh, () => eligible && targetRevision == 1,
                cancellation.Token)).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>()).Which;
            unconfirmed.Request.SessionId.Should().Be(target.Authority.SessionId);
            admission.Calls.Should().Be(fault is "late-cancel" ? 1 : 0);
        }
        using (var root = HostActivity.BeginOperation(HostActivityLayer.Desktop, HostOperation.Storage, RequestOrigin.LocalUi))
        {
            var page = await conversation.ReadPageAsync(target.Authority.SessionId, null, 25, f.Token);
            if (fault is "success" or "late-cancel")
            {
                page.Messages.Should().ContainSingle().Which.Body.Should().Be(body);
                if (unconfirmed is not null) { page.Messages.Single().Request.Should().Be(unconfirmed.Request); }
            }
            else { page.Messages.Should().BeEmpty(); }
            (await conversation.ReadDraftAsync(target.Authority.SessionId, f.Token)).Draft.Revision.Should().Be(0);
        }
        await workflow.Invoking(w => w.Submit(prepared, Fresh, () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Count("conversation_messages").Should().Be(fault is "success" or "late-cancel" ? 1 : 0);
        var audits = WindowsSqliteConversationTests.AuditText(f);
        audits.Should().NotContain(body).And.NotContain("synthetic-only-value");
        if (fault is "credential")
        { audits.Should().Contain("conversation.credential-rejected").And.NotContain(prepared.Expanded.Digest); }
        if (fault is "success") { audits.Should().Contain("conversation.submit").And.Contain("conversation.terminal"); }
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        if (fault is "late-cancel")
        {
            await new HostTaskCoordinator(f.Tasks).RecoverAsync(100, f.Token);
            (await f.Store.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token))
                .Messages.Single().Reason.Should().Be(ModelTurnReason.HostInterrupted);
        }
        f.Count("conversation_messages").Should().Be(fault is "success" or "late-cancel" ? 1 : 0);
    }

    private static Task<bool> Fresh(ArtifactDefinition source, CancellationToken token) => Task.FromResult(true);

    private sealed class Checkpoint(string fault, Action changed) : IHostInteractionTransactionCheckpoint
    {
        public void BeforeAudit(SqliteConnection connection, SqliteTransaction transaction)
        { if (fault is "audit") { throw new IOException("Required audit unavailable"); } }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction) => changed();
    }

    private sealed class Admission(Action observed) : IModelTurnAdmission
    {
        internal int Calls { get; private set; }
        internal ModelContextEnvelope? Context { get; private set; }
        public bool IsQuiescent => true;
        public Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
            ModelHandoffReview? review, CancellationToken token)
        {
            Calls++;
            Context = context;
            HostActivity.RequireCurrent().Request.Should().Be(context.Request);
            context.RequireCompleteInputLimit();
            observed();
            return Task.FromResult(new ModelTurnAdmission(new(ModelTurnOutcome.Unavailable, ModelTurnReason.GenerationNotEnabled), null));
        }
    }
}
