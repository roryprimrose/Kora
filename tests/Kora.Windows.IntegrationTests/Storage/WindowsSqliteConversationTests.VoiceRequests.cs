using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Fact]
    public async Task ActivatedVoiceRetainsExactOriginalOriginAndTypedAuditAcrossRestartWithoutNativeDraftAuthority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var request = new HostRequest(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.ActivatedVoice);
        const string body = "  Original VOICE e\u0301🙂\r\n\tend  ";
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            var retained = await f.Store.RetainConversationMessageAsync(request, new(1), 0, body, static () => true, f.Token);
            retained.Message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            retained.Message.Validate();
            var terminal = await f.Store.CompleteConversationSubmissionAsync(request, ModelTurnOutcome.Unavailable,
                ModelTurnReason.UnregisteredModel, static () => true, f.Token);
            terminal.Task.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            terminal.Task.State.Should().Be(HostTaskState.Unavailable);
        }
        var audits = AuditText(f);
        audits.Should().Contain("\"Initiator\":" + (int)SecurityAuditInitiator.VoiceCommand)
            .And.NotContain(body);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var page = await f.Store.ReadConversationPageAsync(request.SessionId, null, 25, f.Token);
        page.Messages.Single().Body.Should().Be(body);
        page.Messages.Single().Request.Should().Be(request);
        page.Messages.Single().Reason.Should().Be(ModelTurnReason.UnregisteredModel);
        var draftRequest = new HostRequest(new(Guid.NewGuid()), request.SessionId, new(Guid.NewGuid()), RequestOrigin.ActivatedVoice);
        using var draftRoot = HostActivity.BeginRoot(draftRequest, HostActivityLayer.Application, HostOperation.Request);
        await f.Store.RecordControlIntentAsync(draftRequest, f.Token);
        var draft = await f.Store.ReadConversationDraftAsync(request.SessionId, f.Token);
        await f.Store.Invoking(store => store.CommitConversationDraftAsync(draftRequest, draft, "not native",
            static () => true, f.Token).AsTask()).Should().ThrowAsync<InvalidOperationException>();
        f.Count("conversation_drafts").Should().Be(0);
    }

    [Fact]
    public async Task VoiceCredentialRejectionRetainsOnlyOriginalSourceContentFreeDenialAcrossRestart()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var request = new HostRequest(new(Guid.NewGuid()), f.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.ActivatedVoice);
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.Invoking(store => store.RetainConversationMessageAsync(request, new(1), 0,
                "api_key=synthetic-voice-only", static () => true, f.Token).AsTask()).Should().ThrowAsync<ConversationCredentialException>();
        }
        f.Count("conversation_messages").Should().Be(0);
        (await f.Tasks.ReadTaskAsync(request.TaskId, f.Token))!.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        AuditText(f).Should().Contain("conversation.credential-rejected").And.NotContain("synthetic-voice-only");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        (await f.Tasks.ReadTaskAsync(request.TaskId, f.Token))!.State.Should().Be(HostTaskState.Denied);
    }
}
