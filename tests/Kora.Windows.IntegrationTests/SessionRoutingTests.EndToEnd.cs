using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Configuration;
using Kora.Application.Interaction;
using Kora.Application.Voice;
using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Tools;
using Kora.Core.Voice;
using Kora.Windows.Storage;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionRoutingTests
{
    [Fact]
    public async Task NonforegroundWakeInfersReviewsSendsInvokesReadOnlyToolThenAnswersSeparateHostCardAndReadsRetainedBrief()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var capture = new RoutingReplyCapture();
            await using var f = new WakeNativeComposition(capture);
            var speech = new SendReviewSpeech();
            f.Composition.Ui.Native.SpeechOverride = speech;
            await f.Initialize(expanded: true, assistantName: "Kora");
            f.Composition.Adapter.AdviceExpected = true;
            var native = f.Composition.Ui.Native;
            await using var settings = new AudioControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var send = new LocalSpokenSendReplyPreferences(native);
            send.Save(true);
            native.Main.BindSpokenSendReplyConfiguration(new(send, settings, native.Audit));
            var selected = f.Composition.Ui.Model.SelectedSessionRecord;
            f.Composition.Ui.Model.ComposerBody = "independent unsent input";
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => opening.TrySetResult(window));
            var authorization = new HostAuthorizationService(native.Interactions, TimeProvider.System);
            var tools = 0;
            QuestionWindow? cardWindow = null;
            f.Composition.Adapter.AnswerTools = async (request, invokeTool, token) =>
            {
                request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
                request.SessionId.Should().Be(f.Composition.Ui.First.Authority.SessionId);
                var reply = await invokeTool(ReadOnlyCapabilityCatalog.Version, "{}", token);
                reply.Outcome.Should().Be(CapabilityOutcome.Succeeded);
                reply.Version.Should().NotBeNull();
                tools++;

            };
            await PrepareNonforegroundEndToEndWake(f);
            try
            {
                await CompleteEndToEndWake(f, "compare exact original plans");
                var review = await AwaitEndToEndPhase(opening.Task, f, speech);
                review.IsActive.Should().BeFalse();
                f.Composition.Ui.Window!.IsActive.Should().BeFalse();
                Find<ComboBox>(review, "RoutingDestinations").SelectedItem!.ToString().Should().EndWith(": Alpha");
                review.GetVisualDescendants().OfType<TextBlock>().Should().Contain(item =>
                    item.Text != null && item.Text.Contains("inferred", StringComparison.Ordinal));
                Find<Button>(review, "RoutingSubmit").IsEnabled.Should().BeFalse();
                f.Composition.Adapter.Calls.Should().Be(1);
                await Choose(review);
                await AwaitEndToEndPhase(EndToEndSignal(speech.Started.Task), f, speech);
                speech.Text.Should().Be("Send to Alpha: compare exact original plans?");
                capture.Opens.Should().Be(0);
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var status = Find<TextBlock>(review, "RoutingVoiceStatus");
                status.PropertyChanged += (_, args) =>
                {
                    if (args.Property == TextBlock.TextProperty
                        && status.Text?.StartsWith("Speak exactly", StringComparison.Ordinal) == true)
                    { ready.TrySetResult(); }
                };
                speech.Release.SetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
                capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
                capture.Transcript("send");
                await capture.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
                tools.Should().Be(0, "capture must be joined before the original model turn");
                capture.Closing.SetResult();
                await review.VoiceOperation.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
                var next = await f.Stage.NextAsync().WaitAsync(TimeSpan.FromSeconds(15), native.Token);
                tools.Should().Be(1);
                f.Composition.Adapter.Calls.Should().Be(2);
                f.Composition.Adapter.Originals.Should().Equal("compare exact original plans", "compare exact original plans");
                var retained = (await native.Interactions.ReadConversationPageAsync(f.Composition.Ui.First.Authority.SessionId,
                    null, 50, native.Token)).Messages.Should().ContainSingle().Subject;
                retained.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                retained.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
                retained.AssistantResponse!.Body.Should().Be("Genuine second-turn answer");
                capture.IsCaptureQuiescent.Should().BeTrue();

                // A separate Kora-owned operation is presented for the same routed session, not produced by the model.
                var hostRequest = InteractionStorageFixture.NewRequest(retained.Request.SessionId);
                HostQuestionRecord card;
                using (var source = HostActivity.BeginRoot(hostRequest, HostActivityLayer.Desktop, HostOperation.Request))
                {
                    await native.Tasks.CommitAsync(new(hostRequest, new(1), HostTaskState.IntentRecorded), 0, native.Token);
                    var proposal = new HostOperationProposal(hostRequest, new(Guid.NewGuid()), new(1),
                        InteractionStorageFixture.Binding(), HostOperationEffect.BoundedRead, DateTimeOffset.UtcNow.AddMinutes(5));
                    await native.Interactions.PublishTrustedSnapshotAsync(hostRequest, new(true, true, false, true),
                        proposal, 0, native.Token);
                    card = (await authorization.PresentAsync(hostRequest, native.Token)).Question!;
                }
                card.Key.Request.SessionId.Should().Be(retained.Request.SessionId);
                card.Spec.SourceId.Should().Be("host-operation");
                var hostModel = CreateEndToEndCard(card, native.Interactions, authorization);
                cardWindow = new QuestionWindow(hostModel, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
                cardWindow.Show();
                HeadlessSession.SetActivation(cardWindow, true);
                Dispatcher.UIThread.RunJobs();
                await hostModel.OperationQuiescence.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
                hostModel.IsEditable.Should().BeTrue();
                hostModel.HasPendingOperation.Should().BeFalse();
                var capturedApprovals = 0;
                HostInteractionDecision? approvalReview = null;
                native.Main.BindSpokenToolApprovals(() =>
                {
                    capturedApprovals++;
                    var target = hostModel.CaptureSpokenToolApproval(static () => true);
                    target.Should().NotBeNull();
                    return target is null ? [] : [target with
                    {
                        ReviewAsync = async (channel, admitted) =>
                            approvalReview = await target.ReviewAsync(channel, admitted),
                    }];
                });
                await CompleteEndToEndWake(f, "Kora, approve once", next);
                _ = await AwaitEndToEndPhase(f.Stage.NextAsync(), f, speech);
                capturedApprovals.Should().Be(1, native.Main.Transcript);
                approvalReview.Should().NotBeNull();
                approvalReview!.Outcome.Should().Be(HostInteractionOutcome.Presented, approvalReview.Reason);
                var approved = await AwaitEndToEndPhase(hostModel.Completion, f, speech);
                approved.Outcome.Should().Be(HostInteractionOutcome.Approved);
                approved.Question!.AnswerChannel.Should().Be(RequestOrigin.ActivatedVoice);
                approved.Grant!.ApprovedProposal.Request.Should().Be(hostRequest);
                var approvalAudit = ReadEndToEndAudit(native.LocalRoot, "approval.confirm").Last();
                approvalAudit.GetProperty("Request").Deserialize<HostRequest>().Should().Be(hostRequest);
                approvalAudit.GetProperty("ApprovalAnswerChannel").GetInt32().Should().Be((int)RequestOrigin.ActivatedVoice);
                speech.Utterances.Should().Contain(text => text.Contains("Approve this operation once.", StringComparison.Ordinal));
                cardWindow.Close();
                await native.Main.DisableListeningFromTrayAsync();
                await f.Voice.Completion.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
                await f.Composition.Ui.Model.ReadConversationAsync();
                f.Composition.Ui.Model.SelectedAnswerRecord = f.Composition.Ui.Model.ConversationMessages.Single();
                f.Composition.Ui.Model.CanReadSelectedAnswer.Should().BeTrue();
                await f.Composition.Ui.Model.ReadSelectedAnswerAsync();
                native.Main.LastResponseReadOutcome.Should().Be(SpeechReadOutcome.Played,
                    f.Composition.Ui.Model.AnswerReadStatus);
                speech.Utterances.Should().Contain("Genuine second-turn answer");
                f.Composition.Adapter.Calls.Should().Be(2, "approval and retained response read never call the model");
                f.Composition.Ui.Model.SelectedSessionRecord.Should().Be(selected);
                f.Composition.Ui.Model.ComposerBody.Should().Be("independent unsent input");
                (await native.Interactions.ReadConversationPageAsync(f.Composition.Ui.Second.Authority.SessionId,
                    null, 50, native.Token)).Messages.Should().BeEmpty();
                f.Diagnostics.Failure.Should().BeNull();
            }
            finally
            {
                cardWindow?.Close();
                capture.Closing?.TrySetResult();
                speech.Release.TrySetResult();
                await native.Main.DisableListeningFromTrayAsync();
                await f.Voice.Completion.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            }
        });
    }

    [Fact]
    public async Task NamedApproveAllWakeReadsBackThenAutomaticallyRetainsOriginalVoiceRequest()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var capture = new RoutingReplyCapture();
            await using var f = new WakeNativeComposition(capture);
            var speech = new SendReviewSpeech();
            f.Composition.Ui.Native.SpeechOverride = speech;
            f.Composition.Ui.Native.Interactions.BindSessionPermissionDefault(static () => SessionPermissionLevel.ApproveAll);
            await f.Initialize(expanded: true, assistantName: "Kora");
            var native = f.Composition.Ui.Native;
            await using var conversation = new SessionConversationService(native.Interactions, native.Interactions,
                native.Sessions, native.Access, f.Composition.Turns, TimeProvider.System,
                NullLogger<SessionConversationService>.Instance, execution: f.Composition.Turns,
                cancellationAudit: native.Audit, interactions: native.Interactions);
            native.Main.BindSessionCommands(native.Sessions, new(native.Sessions, conversation, native.Access, TimeProvider.System));
            native.Main.BindSpokenSendReview(admitted =>
            {
                var workbench = f.Composition.Ui.Window!;
                var panel = new SessionRequestReviewPanel();
                panel.Bind(native.Main);
                var shown = false;
                workbench.ShowSessions(expanded: true, activate: false, preservePresentation: true);
                workbench.ShowNativeReview(panel, "Review original request", panel, admitted, value => shown = value, activate: false);
                return (() => shown && admitted(), () =>
                {
                    workbench.RemoveNativeReview(panel);
                    panel.Bind(null);
                });
            });
            await PrepareNonforegroundEndToEndWake(f);
            var fallback = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((_, _) => fallback++);
            try
            {
                await CompleteEndToEndWake(f, "in Alpha, compare exact named plans");
                await AwaitEndToEndPhase(EndToEndSignal(speech.Started.Task), f, speech);
                speech.Text.Should().Be("Sending to Alpha: compare exact named plans. Say cancel to stop.");
                native.Main.ForegroundRequestReviewText.Should().Contain("compare exact named plans");
                f.Composition.Ui.Window!.IsActive.Should().BeFalse();
                f.Composition.Adapter.Calls.Should().Be(0);
                capture.Opens.Should().Be(1);
                capture.Closing = new(TaskCreationOptions.RunContinuationsAsynchronously);
                speech.Release.SetResult();
                await capture.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
                f.Composition.Adapter.Calls.Should().Be(0);
                capture.Closing.SetResult();
                _ = await f.Stage.NextAsync().WaitAsync(TimeSpan.FromSeconds(15), native.Token);
                var message = (await native.Interactions.ReadConversationPageAsync(f.Composition.Ui.First.Authority.SessionId,
                    null, 50, native.Token)).Messages.Should().ContainSingle().Subject;
                message.Body.Should().Be("compare exact named plans");
                message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
                message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                message.AssistantResponse!.Body.Should().Be("Genuine second-turn answer");
                var automatic = ReadEndToEndAudit(native.LocalRoot, "approval.automatic").Should().ContainSingle().Subject;
                automatic.GetProperty("Request").Deserialize<HostRequest>().Should().Be(message.Request);
                var details = automatic.GetProperty("Audit").GetProperty("AutomaticApproval");
                details.GetProperty("Basis").GetString().Should().Be("approve-all");
                details.GetProperty("GrantsEvaluated").GetBoolean().Should().BeFalse();
                details.GetProperty("ContentDigest").GetString().Should().Be(
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(message.Body))));
                details.GetProperty("Policy").GetProperty("Level").GetInt32().Should().Be((int)SessionPermissionLevel.ApproveAll);
                f.Composition.Adapter.Calls.Should().Be(1);
                fallback.Should().Be(0, "the exact named request does not use inference or a fallback destination");
                capture.IsCaptureQuiescent.Should().BeTrue();
                f.Diagnostics.Failure.Should().BeNull();
            }
            finally
            {
                capture.Closing?.TrySetResult();
                speech.Release.TrySetResult();
                await native.Main.DisableListeningFromTrayAsync();
                await f.Voice.Completion.WaitAsync(TimeSpan.FromSeconds(15), native.Token);
            }
        });
    }

    private static NativeQuestionViewModel CreateEndToEndCard(HostQuestionRecord question,
        Kora.Windows.Storage.WindowsSqliteHostInteractionStore store, HostAuthorizationService authorization) =>
        new(question, new HostQuestionService(store, TimeProvider.System), authorization, new(store, TimeProvider.System),
            static () => true, TimeProvider.System, NullLogger<NativeQuestionViewModel>.Instance);

    private static async Task CompleteEndToEndWake(WakeNativeComposition fixture, string text, NativeWakeSession? session = null)
    {
        session ??= await fixture.Stage.NextAsync().WaitAsync(TimeSpan.FromSeconds(15), fixture.Composition.Ui.Native.Token);
        session.Wake();
        await session.Capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(15), fixture.Composition.Ui.Native.Token);
        session.Capture.Transcript(text);
        session.Capture.Done.SetResult();
    }

    private static async Task PrepareNonforegroundEndToEndWake(WakeNativeComposition fixture)
    {
        _ = await fixture.Stage.NextAsync().WaitAsync(TimeSpan.FromSeconds(15), fixture.Composition.Ui.Native.Token);
        await fixture.Composition.Ui.Native.Main.SetVoiceConsentAsync(false);
        HeadlessSession.SetActivation(fixture.Composition.Ui.Window!, false);
        await fixture.Composition.Ui.Native.Main.SetVoiceConsentAsync(true);
        Dispatcher.UIThread.RunJobs();
        HeadlessSession.SetActivation(fixture.Composition.Ui.Window!, false);
    }

    private static async Task<bool> EndToEndSignal(Task signal)
    {
        await signal;
        return true;
    }

    private static async Task<T> AwaitEndToEndPhase<T>(Task<T> phase, WakeNativeComposition fixture, SendReviewSpeech speech)
    {
        try { return await phase.WaitAsync(TimeSpan.FromSeconds(15), fixture.Composition.Ui.Native.Token); }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException("End-to-end phase did not finish. Observed wake state: " + fixture.Voice.State
                + "; response: " + fixture.Composition.Ui.Native.Main.ResponseTitle
                + "; transcript: " + fixture.Composition.Ui.Native.Main.Transcript
                + "; speech: " + speech.Text + "; adapter calls: " + fixture.Composition.Adapter.Calls
                + "; failure: " + fixture.Diagnostics.Failure, exception);
        }
    }

    private static List<JsonElement> ReadEndToEndAudit(string root, string action)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root, HostInteractionSchema.Partition, HostInteractionSchema.FileName),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM security_audit_events ORDER BY sequence;";
        using var rows = command.ExecuteReader();
        List<JsonElement> results = [];
        while (rows.Read())
        {
            using var document = JsonDocument.Parse(rows.GetString(0));
            if (string.Equals(document.RootElement.GetProperty("Audit").GetProperty("ActionId").GetString(), action, StringComparison.Ordinal))
            { results.Add(document.RootElement.Clone()); }
        }
        return results;
    }
}
