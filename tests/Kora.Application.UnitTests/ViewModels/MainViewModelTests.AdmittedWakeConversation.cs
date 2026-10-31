using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Hosting.SessionRouting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("capture")]
    [InlineData("join")]
    [InlineData("dispatch")]
    [InlineData("review")]
    [InlineData("answer")]
    public async Task ReplacingEligibleCallOwnerCannotRenewTheOriginalWakeOperation(string phase)
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        f.ViewModel.BindCallOwnershipGate(static () => true);
        await using var conversation = new WakeConversation(f, () => voice.IsCaptureQuiescent);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (phase is "dispatch")
        {
            f.Dispatcher.BeforeInvokeAsync = async () =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TestContext.Current.CancellationToken);
            };
        }
        if (phase is "review")
        {
            conversation.BeforeSelection = async () =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TestContext.Current.CancellationToken);
            };
        }
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.HoldClose = phase is "join";
        try
        {
            first.Wake.SetResult(new(first.Binding.Generation, 12, true));
            await first.CaptureStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            if (phase is not "capture")
            {
                first.Result.SetResult(new("compare these plans", 1, first.Binding.Generation));
                if (phase is "join") { await first.Closing.Task.WaitAsync(TestContext.Current.CancellationToken); }
                if (phase is "dispatch" or "review") { await entered.Task.WaitAsync(TestContext.Current.CancellationToken); }
                if (phase is "answer")
                {
                    await conversation.Adapter.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
                    conversation.Turns.IsQuiescent.Should().BeFalse();
                    conversation.Store.RequestMessage!.AssistantResponse.Should().BeNull();
                }
            }
            var callRevision = f.ViewModel.CallPolicyRevision;
            f.ViewModel.BindCallOwnershipGate(static () => true);
            f.ViewModel.CallPolicyRevision.Should().Be(callRevision);
            first.Result.TrySetResult(new("compare these plans", 1, first.Binding.Generation));
            release.TrySetResult();
            first.ReleaseClose.TrySetResult();
            conversation.Adapter.Release.TrySetResult();
            await Task.WhenAny(voice.Completion, stage.NextAsync());
            if (phase is "answer")
            {
                conversation.Adapter.Calls.Should().Be(1);
                conversation.Store.RequestMessage!.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
                conversation.Store.RequestMessage.AssistantResponse.Should().BeNull();
                conversation.Store.TaskResult!.Task.State.Should().NotBe(HostTaskState.Succeeded);
                f.Audit.Events.Last(item => string.Equals(item.ActionId, "model.turn", StringComparison.Ordinal))
                    .Outcome.Should().NotBe(SecurityAuditOutcome.Succeeded);
            }
            else
            {
                conversation.Adapter.Calls.Should().Be(0);
                conversation.Store.RequestRetentions.Should().Be(0);
                f.Audit.Events.Should().NotContain(item => item.ActionId == "model.turn");
            }
            voice.Completion.IsCompleted.Should().BeTrue("a replacement owner cannot rearm the original utterance");
            await voice.Completion;
            stage.OpenCount.Should().Be(1);
            first.IsQuiescent.Should().BeTrue();
            conversation.Turns.IsQuiescent.Should().BeTrue();
            f.ViewModel.ResponseBody.Should().NotContain(WakeAnswerAdapter.Answer);
            f.Reasoner.Requests.Should().BeEmpty();
        }
        finally
        {
            release.TrySetResult();
            first.ReleaseClose.TrySetResult();
            conversation.Adapter.Release.TrySetResult();
            await voice.StopAsync(TestContext.Current.CancellationToken);
            await voice.Completion;
            await f.ViewModel.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReviewedCommandCannotStealWakeCaptureOrItsPendingPhysicalJoin(bool duringJoin)
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        f.NamePreferences.Name = "Nova";
        f.ViewModel.BindCallOwnershipGate(static () => true);
        f.ViewModel.BindReviewedVoiceCommand(NullLogger<ReviewedVoiceCommandCapture>.Instance, TimeProvider.System);
        await using var conversation = new WakeConversation(f, () => voice.IsCaptureQuiescent);
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        try
        {
            first.HoldClose = duringJoin;
            first.Wake.SetResult(new(first.Binding.Generation, 12, true));
            await first.CaptureStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            if (duringJoin)
            {
                first.Result.SetResult(new("Nova explains these plans", 1, first.Binding.Generation));
                await first.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
            }
            first.Invalidated.Should().Be(duringJoin, "normal closure invalidates PCM before its physical join");
            await f.ViewModel.BeginReviewedVoiceCommandAsync(static () => true);
            f.ViewModel.IsReviewedVoiceCommandActive.Should().BeFalse();
            f.ViewModel.HasReviewedVoiceCommand.Should().BeFalse();
            f.ViewModel.ReviewedVoiceCommandText.Should().BeEmpty();
            await f.ViewModel.CancelReviewedVoiceCommandAsync();
            first.Invalidated.Should().Be(duringJoin, "reviewed-command refusal cannot alter the wake owner's cleanup state");
            voice.IsCaptureQuiescent.Should().BeFalse();
            f.Voice.StartCalls.Should().Be(0);
            conversation.RouteCalls.Should().Be(0);
            conversation.Store.RequestRetentions.Should().Be(0);
            conversation.Adapter.Calls.Should().Be(0);
            f.Audit.Events.Should().NotContain(item => item.ActionId == "model.turn");

            first.Result.TrySetResult(new("Nova explains these plans", 1, first.Binding.Generation));
            first.ReleaseClose.TrySetResult();
            await conversation.Adapter.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            first.IsQuiescent.Should().BeTrue();
            conversation.Adapter.Body.Should().Be("Nova explains these plans");
            conversation.Adapter.Request!.Origin.Should().Be(RequestOrigin.ActivatedVoice);
            conversation.Adapter.Release.TrySetResult();
            await stage.NextAsync();
            conversation.RouteCalls.Should().Be(1);
            conversation.Store.RequestMessage!.AssistantResponse!.Body.Should().Be(WakeAnswerAdapter.Answer);
            f.ViewModel.HasReviewedVoiceCommand.Should().BeFalse();
            f.Reasoner.Requests.Should().BeEmpty();
        }
        finally
        {
            first.ReleaseClose.TrySetResult();
            conversation.Adapter.Release.TrySetResult();
            await voice.StopAsync(TestContext.Current.CancellationToken);
            await voice.Completion;
            await f.ViewModel.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("compare these plans")]
    [InlineData("Nova explains this design")]
    public async Task AdmittedNondefaultWakeCompletesNormalConversationWithUnmodifiedVoiceBodyAfterPhysicalJoin(string body)
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        f.NamePreferences.Name = "Nova";
        await using var conversation = new WakeConversation(f, () => voice.IsCaptureQuiescent);
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.Binding.AssistantName.Should().Be("Nova");
        first.HoldClose = true;
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        first.Result.SetResult(new(body, 1, first.Binding.Generation));
        await first.Closing.Task.WaitAsync(TestContext.Current.CancellationToken);
        conversation.RouteCalls.Should().Be(0);
        conversation.Store.RequestRetentions.Should().Be(0);
        conversation.Adapter.Calls.Should().Be(0);
        voice.IsCaptureQuiescent.Should().BeFalse();

        first.ReleaseClose.SetResult();
        await conversation.Adapter.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        first.IsQuiescent.Should().BeTrue();
        voice.State.Should().Be(HandsFreeVoiceState.CompletingOperation);
        stage.OpenCount.Should().Be(1);
        conversation.Adapter.Body.Should().Be(body);
        conversation.Adapter.Request!.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        conversation.Adapter.Request.SessionId.Should().Be(conversation.Store.Session.Authority.SessionId);
        conversation.Store.RequestMessage!.Body.Should().Be(body);
        conversation.Store.RequestMessage.AssistantResponse.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();

        conversation.Adapter.Release.SetResult();
        await stage.NextAsync();
        conversation.RouteCalls.Should().Be(1);
        conversation.Store.RequestRetentions.Should().Be(1);
        conversation.Store.RequestMessage!.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        conversation.Store.RequestMessage.AssistantResponse!.Body.Should().Be(WakeAnswerAdapter.Answer);
        conversation.Store.TaskResult!.Task.State.Should().Be(HostTaskState.Succeeded);
        f.ViewModel.ResponseBody.Should().Contain(WakeAnswerAdapter.Answer);
        f.ViewModel.ResponseTitle.Should().Be("Request destination: Unnamed conversation.");
        conversation.Turns.IsQuiescent.Should().BeTrue();
        f.Audit.Events.Where(item => string.Equals(item.ActionId, "model.turn", StringComparison.Ordinal)).Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
                SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Voice.StartCalls.Should().Be(0);
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await voice.Completion;
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task NativeActivationDoesNotQualifyAnUnavailableModelOrFallBackToLegacyInference()
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        f.NamePreferences.Name = "Nova";
        await using var conversation = new WakeConversation(f, () => voice.IsCaptureQuiescent, qualified: false);
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        first.Result.SetResult(new("compare these plans", 1, first.Binding.Generation));
        await stage.NextAsync();
        conversation.Store.RequestMessage!.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        conversation.Store.RequestMessage.Reason.Should().Be(ModelTurnReason.QualificationPending);
        conversation.Store.RequestMessage.AssistantResponse.Should().BeNull();
        conversation.Adapter.Calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await voice.Completion;
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("input")]
    [InlineData("session")]
    [InlineData("owner")]
    [InlineData("call")]
    public async Task RevocationDuringNormalDestinationReviewCannotRetainOrRunTheWakeRequest(string change)
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await using var conversation = new WakeConversation(f, () => voice.IsCaptureQuiescent);
        var reviewing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        conversation.BeforeSelection = async () =>
        {
            reviewing.SetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
        };
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        first.Result.SetResult(new("compare these plans", 1, first.Binding.Generation));
        await reviewing.Task.WaitAsync(TestContext.Current.CancellationToken);
        if (change is "input") { f.ViewModel.CommandText = "replacement input"; }
        if (change is "session")
        { f.ViewModel.PublishForegroundConversationTarget(new(conversation.Store.Session, static () => true)); }
        if (change is "owner") { f.ViewModel.BindVoiceOwnershipGate(static () => false); }
        if (change is "call") { f.CallState.SetState(CallState.Unknown); }
        release.SetResult();
        await voice.Completion;
        conversation.RouteCalls.Should().Be(1);
        conversation.Store.RequestRetentions.Should().Be(0);
        conversation.Adapter.Calls.Should().Be(0);
        f.Audit.Events.Should().NotContain(item => item.ActionId == "model.turn");
        f.Reasoner.Requests.Should().BeEmpty();
        stage.OpenCount.Should().Be(1);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task LateOrdinaryModelAnswerCannotDiscloseOrRetainSuccessAfterWakeSourceRetirement()
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await using var conversation = new WakeConversation(f, () => voice.IsCaptureQuiescent);
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        first.Result.SetResult(new("compare these plans", 1, first.Binding.Generation));
        await conversation.Adapter.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        f.ViewModel.CommandText = "replacement input";
        voice.InvalidateCapture();
        voice.IsCaptureQuiescent.Should().BeTrue("the microphone is already physically closed");
        conversation.Turns.IsQuiescent.Should().BeFalse("cancellation is not the provider join");
        voice.Completion.IsCompleted.Should().BeFalse();
        stage.OpenCount.Should().Be(1);
        conversation.Adapter.Release.SetResult();
        await voice.Completion;
        conversation.Turns.IsQuiescent.Should().BeTrue();
        conversation.Store.RequestMessage!.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        conversation.Store.RequestMessage.AssistantResponse.Should().BeNull();
        conversation.Store.TaskResult!.Task.State.Should().NotBe(HostTaskState.Succeeded);
        f.ViewModel.ResponseBody.Should().NotContain(WakeAnswerAdapter.Answer);
        f.Audit.Events.Last(item => string.Equals(item.ActionId, "model.turn", StringComparison.Ordinal)).Outcome.Should().NotBe(SecurityAuditOutcome.Succeeded);
        stage.OpenCount.Should().Be(1);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("Stop", "stop listening")]
    [InlineData("Nova", "disable listening")]
    public async Task NativeWakeKeepsExactDisableListeningPriorityWithoutConversationOrModelWork(string name, string command)
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        f.NamePreferences.Name = name;
        var routes = 0;
        f.ViewModel.BindSessionRouting((_, _, _) =>
        {
            routes++;
            return Task.FromResult<SessionRoutingReceipt?>(null);
        });
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        first.Result.SetResult(new(command, 1, first.Binding.Generation));
        await voice.Completion;
        first.IsQuiescent.Should().BeTrue();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        stage.OpenCount.Should().Be(1);
        routes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Audit.Events.Should().NotContain(item => item.ActionId == "model.turn");
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("input")]
    [InlineData("rename")]
    [InlineData("device")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("consent")]
    [InlineData("call")]
    [InlineData("session")]
    [InlineData("retired-session")]
    [InlineData("retired-capture")]
    public async Task QueuedNativeUtteranceCannotReachRoutingAfterItsOriginalHostSourceChanges(string change)
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        f.NamePreferences.Name = "Nova";
        f.Voice.Microphones = [new("mic", "Headset"), new("other", "Other")];
        var store = new SessionCommandStore(f);
        var target = new ForegroundConversationTarget(store.Session, static () => true);
        f.ViewModel.PublishForegroundConversationTarget(target);
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Dispatcher.BeforeInvokeAsync = async () =>
        {
            queued.SetResult();
            await release.Task.WaitAsync(TestContext.Current.CancellationToken);
        };
        var routes = 0;
        f.ViewModel.BindSessionRouting((_, _, _) =>
        {
            routes++;
            return Task.FromResult<SessionRoutingReceipt?>(null);
        });
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        first.Result.SetResult(new("compare these plans", 1, first.Binding.Generation));
        await queued.Task.WaitAsync(TestContext.Current.CancellationToken);
        first.IsQuiescent.Should().BeTrue();
        if (change is "input") { f.ViewModel.CommandText = "replacement input"; }
        if (change is "rename") { await f.ViewModel.SetAssistantNameAsync("Atlas"); }
        if (change is "device") { f.ViewModel.SelectedMicrophone = f.ViewModel.Microphones.Single(item => string.Equals(item.Id, "other", StringComparison.Ordinal)); }
        if (change is "owner") { f.ViewModel.BindVoiceOwnershipGate(static () => false); }
        if (change is "privacy") { f.Session.IsUnlocked = false; }
        if (change is "consent") { await f.ViewModel.SetVoiceConsentAsync(false); }
        if (change is "call") { f.CallState.SetState(CallState.Unknown); }
        if (change is "session")
        { f.ViewModel.PublishForegroundConversationTarget(new(store.Session, static () => true)); }
        if (change is "retired-session") { target.Retire(); }
        if (change is "retired-capture") { voice.InvalidateCapture(); }
        release.SetResult();
        await voice.Completion;
        routes.Should().Be(0);
        store.RequestRetentions.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Audit.Events.Should().NotContain(item => item.ActionId == "model.turn");
        stage.OpenCount.Should().Be(1);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenericProviderCannotUseEvidenceOrBareTextAsNativeActivation(bool fabricated)
    {
        var stage = new HandsFreeStage { IsAvailable = false };
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        f.NamePreferences.Name = "Nova";
        var routes = 0;
        f.ViewModel.BindSessionRouting((_, _, _) =>
        {
            routes++;
            return Task.FromResult<SessionRoutingReceipt?>(null);
        });
        await f.ViewModel.InitializeAsync();
        await f.RaiseActivatedTranscriptAsync(fabricated ? "Nova, compare these plans" : "compare these plans",
            1, fabricated ? new UnknownWakeEvidence() : null);
        routes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Audit.Events.Should().NotContain(item => item.ActionId == "model.turn");
        stage.OpenCount.Should().Be(0);
        if (!fabricated)
        {
            await f.RaiseActivatedTranscriptAsync("Nova, compare these plans", 1);
            routes.Should().Be(1, "generic activated voice retains the existing literal-prefix route");
        }
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task FailedNativeClosureCannotMintActivationOrStartNormalConversation()
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        var voice = decorated!;
        var routes = 0;
        f.ViewModel.BindSessionRouting((_, _, _) =>
        {
            routes++;
            return Task.FromResult<SessionRoutingReceipt?>(null);
        });
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.FailClose = true;
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        first.Result.SetResult(new("compare these plans", 1, first.Binding.Generation));
        await voice.Completion;
        routes.Should().Be(0);
        voice.IsCaptureQuiescent.Should().BeFalse();
        voice.State.Should().Be(HandsFreeVoiceState.RecoveryRequired);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Audit.Events.Should().NotContain(item => item.ActionId == "model.turn");
        stage.OpenCount.Should().Be(1);
        f.ViewModel.Dispose();
        await FluentActions.Awaiting(async () => await voice.DisposeAsync()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AdmittedWakeWithoutOrdinaryConversationCompositionDoesNotOpenLegacyInference()
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        await f.ViewModel.InitializeAsync();
        var first = await stage.NextAsync();
        first.Wake.SetResult(new(first.Binding.Generation, 12, true));
        first.Result.SetResult(new("compare these plans", 1, first.Binding.Generation));
        await stage.NextAsync();
        f.ViewModel.ResponseTitle.Should().Be("Session routing unavailable.");
        f.Reasoner.Requests.Should().BeEmpty();
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await voice.Completion;
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("channel")]
    [InlineData("call-owner")]
    [InlineData("call-owner-loss")]
    [InlineData("dictation-retirement")]
    public async Task NativeRoutingRechecksItsOriginalChannelAndAdmissionAtEntry(string change)
    {
        var stage = new HandsFreeStage();
        HandsFreeVoiceRecognitionService? decorated = null;
        var f = CreateHandsFreeFixture(stage, value => decorated = value);
        await using var voice = decorated!;
        var callOwnerEligible = true;
        if (change is "call-owner-loss")
        { f.ViewModel.BindCallOwnershipGate(() => callOwnerEligible); }
        var calls = 0;
        f.ViewModel.BindSessionRouting((_, _, _) =>
        {
            calls++;
            return Task.FromResult<SessionRoutingReceipt?>(null);
        });
        await f.ViewModel.InitializeAsync();
        var opened = await stage.NextAsync();
        Kora.Core.Diagnostics.HostActivity? differentChannel = null;
        void WindowRequested(object? sender, Kora.Application.ViewModels.WindowAction action)
        {
            if (change is "channel" && action == Kora.Application.ViewModels.WindowAction.ShowPresence && differentChannel is null)
            {
                differentChannel = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                    Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
            }
        }
        f.ViewModel.WindowActionRequested += WindowRequested;
        if (change is "call-owner")
        { f.Dispatcher.BeforeInvoke = () => f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (change is "call-owner-loss")
        { f.Dispatcher.BeforeInvoke = () => callOwnerEligible = false; }
        var dictation = change is "dictation-retirement"
            ? f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance) : null;
        var armed = true;
        void Retired(object? sender, EventArgs args)
        {
            if (!armed) { return; }
            armed = false;
            f.ViewModel.BindVoiceOwnershipGate(static () => false);
        }
        if (dictation is not null) { dictation.Changed += Retired; }
        var terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = change switch
        {
            "call-owner" => "The command could not be completed.",
            "call-owner-loss" => "Session routing stopped.",
            _ => "Voice request retired.",
        };
        void Presented(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(f.ViewModel.ResponseTitle)
                && string.Equals(f.ViewModel.ResponseTitle, expected, StringComparison.Ordinal))
            {
                differentChannel?.Complete(Kora.Core.Diagnostics.HostOperationOutcome.Cancelled);
                differentChannel?.Dispose();
                differentChannel = null;
                terminal.TrySetResult();
            }
        }
        f.ViewModel.PropertyChanged += Presented;
        try
        {
            opened.Wake.SetResult(new(opened.Binding.Generation, 12, true));
            opened.Result.SetResult(new("compare these plans", 1, opened.Binding.Generation));
            await Task.WhenAny(voice.Completion, terminal.Task);
            terminal.Task.IsCompletedSuccessfully.Should().BeTrue();
            f.ViewModel.ResponseTitle.Should().Be(expected);
            calls.Should().Be(0);
            f.Reasoner.Requests.Should().BeEmpty();
            f.Audit.Events.Should().NotContain(item => item.ActionId == "model.turn");
        }
        finally
        {
            f.ViewModel.PropertyChanged -= Presented;
            f.ViewModel.WindowActionRequested -= WindowRequested;
            if (dictation is not null) { dictation.Changed -= Retired; }
            await voice.StopAsync(TestContext.Current.CancellationToken);
            await voice.Completion;
            f.ViewModel.Dispose();
        }
        opened.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task OrdinaryRoutingOwnsAndDisposesItsPresentationLeaseThroughRouteCompletion()
    {
        var f = await Fixture.CreateInitializedAsync();
        var lease = new WakePresentationLease();
        var calls = 0;
        f.ViewModel.BindSessionRouting((body, origin, admitted) =>
        {
            body.Should().Be("compare these plans");
            origin.Should().Be(RequestOrigin.LocalUi);
            admitted().Should().BeTrue();
            lease.Disposed.Should().BeFalse();
            calls++;
            return Task.FromResult<SessionRoutingReceipt?>(null);
        }, () => lease);
        await f.RunAsync("compare these plans");
        calls.Should().Be(1);
        lease.Disposed.Should().BeTrue();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
    }

    private sealed class WakePresentationLease : IDisposable
    {
        internal bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class UnknownWakeEvidence : IVoiceActivationEvidence
    {
        public long Generation => 1;
    }

    private sealed class WakeClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    private sealed class WakeConversation : IAsyncDisposable
    {
        internal SessionCommandStore Store { get; }
        internal WakeAnswerAdapter Adapter { get; }
        internal ModelTurnHost Turns { get; }
        internal int RouteCalls { get; private set; }
        internal Func<Task>? BeforeSelection { get; set; }
        private readonly SessionConversationService conversation;
        private readonly SessionRoutingService routing;

        internal WakeConversation(Fixture fixture, Func<bool> captureJoined, bool qualified = true)
        {
            Store = new(fixture);
            Adapter = new(captureJoined);
            var clock = new WakeClock();
            var workspace = new SessionWorkspaceService(Store, new(fixture.HostStore), Store,
                NullLogger<SessionWorkspaceService>.Instance);
            var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(Store, new(), new(), new(fixture.ApplicationInfo),
                new(dependencies), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            var modes = new ProviderModeConfigurationService(fixture.ProviderPreferences, fixture.OutputAdmission, fixture.Audit);
            modes.Observe();
            Turns = new(Store, Store, Store, registry, fixture.Audit, NullLogger<ModelTurnHost>.Instance, clock,
                [new(ModelProviderSelection.OllamaCandidate, qualified
                    ? ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost
                    : ModelQualificationGate.None, clock.GetUtcNow().AddMinutes(5), Adapter)], modes);
            conversation = new(Store, Store, workspace, Store, Turns, clock,
                NullLogger<SessionConversationService>.Instance, execution: Turns);
            routing = new(workspace, conversation, Store, clock);
            fixture.ViewModel.BindSessionRouting(async (body, origin, admitted) =>
            {
                RouteCalls++;
                var review = await routing.PrepareAsync(body, origin, admitted, TestContext.Current.CancellationToken);
                if (BeforeSelection is { } before) { await before(); }
                var selection = await routing.SelectAsync(review, Store.Session.Authority.SessionId, TestContext.Current.CancellationToken);
                return await routing.SubmitAsync(selection, TestContext.Current.CancellationToken);
            });
        }

        public async ValueTask DisposeAsync()
        {
            routing.Retire();
            await conversation.DisposeAsync();
            await Turns.DisposeAsync();
        }
    }

    private sealed class WakeAnswerAdapter(Func<bool> captureJoined) : IModelTurnAdapter
    {
        internal const string Answer = "Answer from the ordinary admitted model turn.";
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        internal string? Body { get; private set; }
        internal HostRequest? Request { get; private set; }

        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            captureJoined().Should().BeTrue();
            Calls++;
            Request = provenance.Request;
            using var document = JsonDocument.Parse(context);
            Body = document.RootElement.GetProperty("UserRequest").GetString();
            document.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(0);
            Entered.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(ModelTurnOutcome.Succeeded, provenance.Selection, new(Answer, null));
        }
    }
}
