using System.Diagnostics;
using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Hosting.SessionRouting;
using Kora.Core.Diagnostics;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("foreground")]
    [InlineData("named")]
    [InlineData("cancel")]
    [InlineData("native")]
    [InlineData("drift")]
    [InlineData("audit-failure")]
    [InlineData("playback-failure")]
    [InlineData("downgrade-before-readback")]
    [InlineData("no-presenter")]
    public async Task ApproveAllOriginalVoiceReadbackSubmitsOnlyAfterPlaybackAndExactPolicyAudit(string scenario)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        var f = await RoutingReplyHostAsync();
        var store = BindRequestCommands(f);
        var readbackStore = new ReadbackConversationStore(store);
        var workspace = new SessionWorkspaceService(store, new(f.HostStore), store, NullLogger<SessionWorkspaceService>.Instance);
        await using var conversation = new SessionConversationService(readbackStore, store, workspace, store,
            new RequestAdmission(), TimeProvider.System, NullLogger<SessionConversationService>.Instance);
        f.ViewModel.BindSessionCommands(workspace, new(workspace, conversation, store, TimeProvider.System));
        f.ViewModel.CanConfirmForegroundRequest.Should().BeFalse();
        var policy = new SessionPermissionPolicy(store.Session.Authority.SessionId, new(1), new(1), SessionPermissionLevel.ApproveAll);
        await store.SetSessionPermissionAsync(HostRequest.Create(RequestOrigin.LocalUi), policy with { Level = SessionPermissionLevel.ReviewAll },
            SessionPermissionLevel.ApproveAll, static () => true, TestContext.Current.CancellationToken);
        if (scenario is "named")
        {
            await f.RunAsync("session rename " + store.Session.Authority.SessionId.Value.ToString("D") + " 1 0 \"Build plan\"");
        }
        f.ViewModel.PublishForegroundConversationTarget(new(store.Session, static () => true));
        var playback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var policyReads = 0;
        var presented = false;
        store.AfterPermissionRead = () =>
        {
            policyReads++;
            if (scenario is "downgrade-before-readback" && policyReads == 1)
            {
                store.SetSessionPermissionAsync(HostRequest.Create(RequestOrigin.LocalUi), store.PermissionPolicy!,
                    SessionPermissionLevel.ReviewAll, static () => true, TestContext.Current.CancellationToken)
                    .AsTask().IsCompletedSuccessfully.Should().BeTrue();
            }
        };
        var statuses = new List<string>();
        f.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(f.ViewModel.SessionRequestVoiceReplyStatus), StringComparison.Ordinal))
            { statuses.Add(f.ViewModel.SessionRequestVoiceReplyStatus); }
        };
        if (scenario is not "no-presenter") { f.ViewModel.BindSpokenSendReview(admitted =>
        {
            f.TextToSpeech.SpeakGate = playback;
            presented = true;
            f.ViewModel.IsApproveAllReadback.Should().BeTrue();
            f.ViewModel.CanConfirmForegroundRequest.Should().BeFalse();
            f.ViewModel.ForegroundRequestHeading.Should().Contain("Approve all");
            f.ViewModel.ForegroundRequestReviewText.Should().Contain("Settings-selected provider");
            return (() => admitted(), static () => { });
        }); }
        readbackStore.BeforeBound = () =>
        {
            if (scenario is "drift") { readbackStore.PolicyChanged = true; }
            if (scenario is "audit-failure") { readbackStore.FailAudit = true; }
        };
        var command = scenario is "named" ? "Kora, in Build plan, original body" : "Kora, ask this conversation original body";
        var oldNativeConfirm = f.ViewModel.CaptureForegroundRequestDecision(confirm: true);
        var operation = f.RaiseActivatedTranscriptAsync(command, 1);
        if (scenario is "downgrade-before-readback" or "no-presenter")
        {
            await operation;
            store.RequestRetentions.Should().Be(0);
            f.ViewModel.CanConfirmForegroundRequest.Should().BeTrue();
            f.ViewModel.ForegroundRequestHeading.Should().Contain("nothing sent");
            await f.ViewModel.ConfirmForegroundRequestAsync();
            store.RequestRetentions.Should().Be(1);
            await f.ViewModel.DisposeAsync();
            return;
        }
        await Task.WhenAny(operation, f.TextToSpeech.SpeakStarted.Task)
            .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        f.TextToSpeech.SpeakStarted.Task.IsCompleted.Should().BeTrue(
            $"reads={policyReads}; shown={presented}; states={string.Join("; ", statuses)}; starts={f.Voice.StartCalls}; response={f.ViewModel.ResponseBody}");
        f.TextToSpeech.SpokenText.Should().Contain("Sending to ").And.Contain("original body");
        store.RequestRetentions.Should().Be(0);
        await oldNativeConfirm();
        store.RequestRetentions.Should().Be(0);
        await f.ViewModel.ConfirmForegroundRequestAsync();
        store.RequestRetentions.Should().Be(0);
        Task? nativeDecision = null;
        if (scenario is "cancel") { f.Voice.RaiseTranscript("cancel", 1); }
        else if (scenario is "native") { nativeDecision = f.ViewModel.CancelForegroundRequestAsync(); }
        else if (scenario is "playback-failure") { playback.SetException(new IOException("Synthetic readback failure")); }
        else { playback.SetResult(); }
        await operation;
        if (nativeDecision is not null) { await nativeDecision; }
        if (scenario is "foreground" or "named")
        {
            store.RequestRetentions.Should().Be(1, f.ViewModel.ResponseBody);
            store.RequestMessage!.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            store.AutomaticAudits.Single().Request.Should().Be(store.RequestMessage.Request);
        }
        else if (scenario is "drift")
        {
            store.RequestRetentions.Should().Be(0);
            f.ViewModel.HasForegroundRequestReview.Should().BeTrue();
            f.ViewModel.CanConfirmForegroundRequest.Should().BeTrue();
            f.ViewModel.ForegroundRequestHeading.Should().Contain("nothing sent");
            await f.ViewModel.ConfirmForegroundRequestAsync();
            store.RequestRetentions.Should().Be(1, f.ViewModel.ResponseBody);
        }
        else if (scenario is "audit-failure")
        {
            store.RequestRetentions.Should().Be(0);
            f.ViewModel.ResponseTitle.Should().Be("Session command not confirmed.");
        }
        else { store.RequestRetentions.Should().Be(0); }
        f.Voice.IsCaptureQuiescent.Should().BeTrue();
        await f.ViewModel.DisposeAsync();
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("typed")]
    [InlineData("no-preselection")]
    [InlineData("inferred")]
    [InlineData("last-selected")]
    [InlineData("body")]
    [InlineData("owned")]
    public async Task ApproveAllRoutingReplyRequiresExactOriginalForegroundAndExclusiveCapture(string boundary)
    {
        var f = await RoutingReplyHostAsync();
        var absent = RoutingVoiceReview("absent", RequestOrigin.ActivatedVoice);
        f.ViewModel.CreateApproveAllReadbackReply(absent, static () => true).Should().BeNull();
        f.ViewModel.BindSessionRouting(async (body, origin, admitted) =>
        {
            var sample = RoutingVoiceReview(body, origin);
            var basis = boundary is "inferred" ? SessionRoutingReview.PreselectionBasis.Inferred
                : boundary is "last-selected" ? SessionRoutingReview.PreselectionBasis.LastSelected
                : SessionRoutingReview.PreselectionBasis.Foreground;
            var review = new SessionRoutingReview(boundary is "body" ? "different original" : body,
                boundary is "typed" ? RequestOrigin.LocalUi : origin, sample.Snapshot, sample.Match,
                sample.Candidates, sample.ExpiresAt, sample.Continue,
                boundary is "no-preselection" ? null : new(sample.Candidates[0], basis));
            if (boundary is "typed")
            { f.ViewModel.CreateApproveAllReadbackReply(review, admitted).Should().BeNull(); return null; }
            if (boundary is "no-preselection" or "inferred" or "last-selected" or "body")
            {
                FluentActions.Invoking(() => f.ViewModel.CreateApproveAllReadbackReply(review, admitted))
                    .Should().Throw<InvalidOperationException>();
                f.Voice.StartedPhrases.Should().BeEmpty();
                return null;
            }
            var reply = f.ViewModel.CreateApproveAllReadbackReply(review, admitted)!;
            if (boundary is "owned")
            {
                FluentActions.Invoking(() => f.ViewModel.CreateApproveAllReadbackReply(review, admitted))
                    .Should().Throw<InvalidOperationException>();
            }
            (await reply.StartAsync())!.Choice.Should().Be(SessionRoutingVoiceReply.SessionRoutingVoiceChoice.Submit);
            await reply.DisposeAsync();
            f.ViewModel.ReleaseSessionRoutingVoiceReply(reply);
            f.TextToSpeech.SpokenText.Should().Contain(body);
            f.Voice.IsCaptureQuiescent.Should().BeTrue();
            return null;
        });
        await f.RaiseActivatedTranscriptAsync("Kora, original route", 1);
        await f.ViewModel.DisposeAsync();
    }

    private sealed class ReadbackConversationStore(SessionCommandStore store) : ISessionConversationStore
    {
        internal Action? BeforeBound { get; set; }
        internal bool PolicyChanged { get; set; }
        internal bool FailAudit { get; set; }
        public async ValueTask<ConversationSubmissionReceipt> RetainPermissionBoundConversationAsync(
            HostRequest request, HostRevision generation, long metadata, string body, IReadOnlyList<HostRequest> predecessors,
            SessionPermissionPolicy expected, AutomaticApprovalAudit approval, Func<bool> admitted, CancellationToken token)
        {
            BeforeBound?.Invoke();
            if (PolicyChanged) { throw new ISessionConversationStore.PermissionChangedException(); }
            if (FailAudit) { throw new IOException("Synthetic atomic audit failure"); }
            await store.RecordAutomaticApprovalAsync(request, expected, approval, admitted, token);
            return await store.RetainConversationMessageAsync(request, generation, metadata, body, admitted, token);
        }
        public ValueTask<ConversationSubmissionReceipt> RetainConversationMessageAsync(HostRequest request,
            HostRevision generation, long metadata, string body, Func<bool> admitted, CancellationToken token) =>
            store.RetainConversationMessageAsync(request, generation, metadata, body, admitted, token);
        public ValueTask<ConversationSubmissionReceipt> CompleteConversationSubmissionAsync(HostRequest request,
            ModelTurnOutcome outcome, ModelTurnReason reason, Func<bool> admitted, CancellationToken token) =>
            store.CompleteConversationSubmissionAsync(request, outcome, reason, admitted, token);
        public ValueTask<ConversationMessage> ReadConversationMessageAsync(HostId<SessionIdentity> session,
            HostId<RequestIdentity> request, CancellationToken token) => store.ReadConversationMessageAsync(session, request, token);
        public ValueTask<HostTaskRecord> RecordConversationCredentialRejectionAsync(HostRequest request,
            HostRevision generation, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> ReadConversationDraftAsync(HostId<SessionIdentity> session,
            CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationDraftSnapshot> CommitConversationDraftAsync(HostRequest control,
            ConversationDraftSnapshot expected, string? body, Func<bool> admitted, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ConversationPage> ReadConversationPageAsync(HostId<SessionIdentity> session,
            ConversationCursor? cursor, int limit, CancellationToken token) => throw new NotSupportedException();
    }
}
