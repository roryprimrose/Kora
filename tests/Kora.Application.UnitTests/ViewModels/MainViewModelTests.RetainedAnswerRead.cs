using System.Collections.Concurrent;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Tools.Speech;
using Kora.Application.Voice;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private static ConversationMessage SpeechAnswer(HostRequest request, string answer = "Retained answer only.")
    {
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "Never read this USER body",
            ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0,
            AssistantResponse = new(provenance, answer)
        };
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task RetainedAnswerSpeaksOnlyExactAssistantBodyAndAuditsOriginalInput(RequestOrigin origin)
    {
        var f = await CreateReadableResponseAsync();
        if (origin == RequestOrigin.ActivatedVoice)
        {
            await f.ViewModel.SetVoiceConsentAsync(true);
            await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
            await f.ViewModel.BeginPushToTalkAsync();
            await f.ViewModel.EndPushToTalkAsync();
        }
        var request = HostRequest.Create(origin);
        var source = SpeechAnswer(request);
        var currentBody = f.ViewModel.ResponseBody;
        var validations = 0;
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var result = await f.ViewModel.ReadAnswerOnceAsync(source, () => true,
            _ => { validations++; return Task.CompletedTask; }, TestContext.Current.CancellationToken);
        result.Should().Contain("completed once");
        validations.Should().Be(1);
        f.TextToSpeech.SpokenText.Should().Be(source.AssistantResponse!.Body);
        f.ViewModel.ResponseBody.Should().Be(currentBody);
        f.Reasoner.Requests.Should().BeEmpty();
        var audits = f.Audit.Events.Where(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)).ToArray();
        audits.Should().HaveCount(2);
        audits[0].Outcome.Should().Be(SecurityAuditOutcome.Requested);
        audits[1].Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
        audits[1].TargetId.Should().Be(audits[0].TargetId);
        audits[1].CorrelationId.Should().Be(audits[0].CorrelationId);
        audits[0].Initiator.Should().Be(origin == RequestOrigin.ActivatedVoice
            ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser);
    }

    [Theory]
    [InlineData("call")]
    [InlineData("visual")]
    [InlineData("owner")]
    [InlineData("source")]
    [InlineData("foreign")]
    [InlineData("oversize")]
    [InlineData("limits")]
    [InlineData("source-at-admission")]
    [InlineData("provider")]
    [InlineData("uncertain")]
    [InlineData("audit")]
    public async Task RetainedAnswerDenialOrUncertainCompletionNeverClaimsSuccessfulReading(string fault)
    {
        var f = await CreateReadableResponseAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var source = SpeechAnswer(fault is "foreign" ? HostRequest.Create(RequestOrigin.LocalUi) : request,
            fault is "oversize" ? string.Join(' ', Enumerable.Repeat("word", 200)) : "Retained answer only.");
        if (fault is "call") { f.CallState.SetState(CallState.Unknown); await f.ViewModel.CallClosureTask; }
        if (fault is "visual") { f.ViewModel.DefaultResponseMode = ResponseOutputMode.VisualOnly; }
        if (fault is "owner") { f.ViewModel.BindCallOwnershipGate(() => false); }
        if (fault is "limits") { f.Preferences.SummaryLimitsLoadFailure = new InvalidDataException(); f.SpeechConfiguration.Reload(); }
        if (fault is "provider") { f.TextToSpeech.SpeakException = new InvalidOperationException("Synthetic unavailable"); }
        if (fault is "uncertain") { f.TextToSpeech.ReturnWhileSpeaking = true; }
        if (fault is "audit")
        {
            f.Audit.BeforeWrite = audit =>
            {
                if (string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal) && audit.Outcome == SecurityAuditOutcome.Succeeded)
                { throw new IOException("Synthetic terminal audit failure"); }
            };
        }
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var result = await f.ViewModel.ReadAnswerOnceAsync(source, () => fault is not "source",
            _ => fault is "source-at-admission" ? Task.FromException(new InvalidOperationException("Source deleted"))
                : Task.CompletedTask, TestContext.Current.CancellationToken);
        result.Should().NotContain("completed once");
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        if (fault is not ("uncertain" or "audit")) { f.TextToSpeech.SpokenText.Should().BeNull(); }
        f.TextToSpeech.IsSpeaking.Should().BeFalse();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("close")]
    public async Task RetainedAnswerCancellationJoinsPlaybackAndSuppressesLateCompletion(string transition)
    {
        var f = await CreateReadableResponseAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var cancellation = new CancellationTokenSource();
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.SpeakStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var read = f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request), () => true, _ => Task.CompletedTask, cancellation.Token);
        await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var duplicate = await f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request), () => true, _ => Task.CompletedTask,
            TestContext.Current.CancellationToken);
        duplicate.Should().Contain("refused");
        if (transition is "cancel") { cancellation.Cancel(); }
        if (transition is "dispose") { f.ViewModel.Dispose(); }
        if (transition is "close") { f.ViewModel.RetireCurrentResponseSpeech(); }
        (await read).Should().NotContain("completed once");
        f.TextToSpeech.IsSpeaking.Should().BeFalse();
        f.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
        var calls = f.TextToSpeech.SpeakCalls;
        f.TextToSpeech.SpeakGate.TrySetResult();
        f.TextToSpeech.SpeakCalls.Should().Be(calls);
    }

    [Theory]
    [InlineData("read selected")]
    [InlineData("read selected answer from clipboard")]
    [InlineData("read selected answer and approve once")]
    [InlineData("read selected\nanswer")]
    public async Task SelectedAnswerMalformedCommandNeverFallsBackToCurrentResponseOrInference(string text)
    {
        var f = await CreateReadableResponseAsync();
        await f.RunAsync(text);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.ReadCurrentResponseStatus.Should().Contain("not accepted");
    }

    [Fact]
    public async Task SelectedAnswerWithoutSelectionClarifiesWithoutCurrentResponseFallback()
    {
        var f = await CreateReadableResponseAsync();
        await f.RunAsync("read selected answer");
        f.ViewModel.ReadCurrentResponseStatus.Should().Contain("Select one exact retained");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RetainedAnswerNoncooperativeSynthesisRemainsOwnedUntilQuiescenceAndCannotPublishLateFrame()
    {
        var f = await CreateReadableResponseAsync();
        f.TextToSpeech.IgnoreSpeakCancellation = true;
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.SpeakStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var read = f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request), () => true, _ => Task.CompletedTask, cancellation.Token);
        await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        read.IsCompleted.Should().BeFalse();
        f.ViewModel.IsCurrentResponseReadActive.Should().BeTrue();
        f.TextToSpeech.PlaybackFrame.Should().Be(SpeechPlaybackFrame.Inactive);
        f.TextToSpeech.SpeakGate.SetResult();
        (await read).Should().NotContain("completed once");
        f.TextToSpeech.PlaybackFrame.Should().Be(SpeechPlaybackFrame.Inactive);
        f.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedAnswerTypedAndVoicePreserveCapturedChannelAndNeverUseCurrentResponse(bool voice)
    {
        var f = await CreateReadableResponseAsync();
        string? result = null;
        f.ViewModel.BindSelectedAnswerRead(eligible => async token =>
        {
            eligible().Should().BeTrue();
            var original = HostActivity.RequireCurrent().Request;
            var source = SpeechAnswer(original);
            result = await f.ViewModel.ReadAnswerOnceAsync(source, eligible, _ => Task.CompletedTask, token);
        });
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, read selected answer", 1); }
        else { await f.RunAsync("read selected answer"); }
        result.Should().Contain("completed once");
        f.TextToSpeech.SpokenText.Should().Be("Retained answer only.");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(80, true)]
    [InlineData(81, false)]
    public async Task RetainedAnswerWholeBodyHonorsExactWordCapWithoutTruncation(int words, bool allowed)
    {
        var f = await CreateReadableResponseAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var text = string.Join(' ', Enumerable.Repeat("word", words));
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var result = await f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request, text), () => true,
            _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        result.Contains("completed once", StringComparison.Ordinal).Should().Be(allowed);
        f.TextToSpeech.SpokenText.Should().Be(allowed ? text : null);
    }

    [Fact]
    public async Task RetainedAnswerTraceAndAuditKeepOriginalInputAndExactSourceWithoutContentTags()
    {
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Enqueue
        };
        ActivitySource.AddActivityListener(listener);
        var f = await CreateReadableResponseAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var source = SpeechAnswer(request);
        var auditRequests = new List<HostRequest>();
        f.Audit.BeforeWrite = audit =>
        {
            if (string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal))
            { auditRequests.Add(HostActivity.RequireCurrent().Request); }
        };
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await f.ViewModel.ReadAnswerOnceAsync(source, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        var playback = stopped.Single(item => Equals(item.GetTagItem("kora.source.request.id"), source.Request.RequestId.Value));
        playback.TraceId.Should().Be(root.Activity!.TraceId);
        playback.GetTagItem("kora.source.task.id").Should().Be(source.Request.TaskId.Value);
        playback.GetTagItem("kora.source.revision").Should().Be(source.Revision);
        playback.GetTagItem("kora.session.id").Should().Be(request.SessionId.Value);
        playback.TagObjects.Select(item => item.Value?.ToString()).Should().NotContain(source.Body)
            .And.NotContain(source.AssistantResponse!.Body);
        playback.Baggage.Should().BeEmpty();
        auditRequests.Should().Equal(request, request);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("approval")]
    [InlineData("grant")]
    [InlineData("native")]
    [InlineData("prompt")]
    public async Task SelectedAnswerDeniedPendingDecisionsRemainUnchangedWithoutConsumingTheirEpochs(string pending)
    {
        var f = CreateSummaryFixture(SpeechProviderIds.Windows);
        await f.ViewModel.InitializeAsync();
        if (pending is "question" or "approval")
        {
            if (pending is "question") { f.Reasoner.Question = new("Choose one", ["A", "B"]); }
            else { f.Reasoner.Action = Kora.Core.Commands.BuiltInAction.LockMachine; }
            await f.RunAsync("choose something");
            await f.ViewModel.ActiveReasoningTask!;
        }
        if (pending is "grant")
        {
            await f.ViewModel.PrepareGrantChangeAsync(new(Kora.Core.Commands.GrantChangeOperation.Add,
                Kora.Core.Commands.BuiltInAction.LockMachine, Kora.Core.Commands.ModelApprovalScope.Always));
        }
        if (pending is "native") { f.ViewModel.BindResponseReadDecisionGate(() => false); }
        if (pending is "prompt") { f.ViewModel.BindRetainedAnswerReadDecisionGate(() => false); }
        f.TextToSpeech.ClearSpokenResponse();
        f.Reasoner.Requests.Clear();
        var question = f.ViewModel.IsModelQuestionPending;
        var approval = f.ViewModel.IsModelActionApprovalPending;
        var grant = f.ViewModel.IsGrantChangePending;
        var body = f.ViewModel.ResponseBody;
        var epoch = typeof(Kora.Application.ViewModels.MainViewModel).GetField("approvalPreparationRevision",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var before = epoch.GetValue(f.ViewModel);
        await f.RunAsync("read selected answer");
        epoch.GetValue(f.ViewModel).Should().Be(before);
        f.ViewModel.IsModelQuestionPending.Should().Be(question);
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.ViewModel.IsGrantChangePending.Should().Be(grant);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectedAnswerDoesNotRetireOrConsumeCompetingComposerDictation()
    {
        var f = CreateVoicePrivacyFixture();
        f.ViewModel.BindVoiceOwnershipGate(() => true);
        f.Voice.IsLiteralDictationAvailable = true;
        await f.ViewModel.InitializeAsync();
        using var composer = DictationComposer(f);
        await using var service = f.ViewModel.BindComposerDictation(NullLogger<ComposerDictationService>.Instance);
        await service.BeginAsync(composer, () => true, TestContext.Current.CancellationToken);
        service.IsActive.Should().BeTrue();
        await f.RunAsync("read selected answer");
        service.IsActive.Should().BeTrue();
        composer.Body.Should().BeEmpty();
        f.ViewModel.ReadCurrentResponseStatus.Should().Contain("dictation owns input");
        f.TextToSpeech.SpokenText.Should().BeNull();
    }
}
