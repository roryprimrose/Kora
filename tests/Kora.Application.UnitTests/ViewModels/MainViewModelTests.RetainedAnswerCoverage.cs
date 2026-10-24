using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.UnitTests.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Voice;
using Kora.Tools.Speech;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task RetainedAnswerRefusesValidRetainedUserOnlyRecordWithoutReadingOrAuditingSpeech()
    {
        var f = await CreateReadableResponseAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var source = new ConversationMessage(request, new(1), 1, 2, DateTimeOffset.UnixEpoch,
            "Original USER body is never speech output.", ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel);
        source.Validate();
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var result = await f.ViewModel.ReadAnswerOnceAsync(source, () => true,
            _ => throw new InvalidOperationException("USER-only records cannot acquire a speech source."),
            TestContext.Current.CancellationToken);

        result.Should().Contain("refused");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResponseReadRejectsEmptyOrStaleHostIssuedTargetBeforePlayback(bool empty)
    {
        var f = await CreateReadableResponseAsync();
        await f.ViewModel.ReadCurrentResponseCommand.ExecuteAsync();
        var receipt = f.Audit.Events.Last(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal));
        f.TextToSpeech.ClearSpokenResponse();
        var audits = f.Audit.Events.Count;
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var target = new ResponseSpeechTarget(empty ? Guid.Empty : Guid.Parse(receipt.TargetId), long.MaxValue,
            f.ViewModel.CallPolicyRevision);

        await ((ISpeechReadOncePlayback)f.ViewModel).ReadOnceAsync(target, SecurityAuditInitiator.LocalUser,
            TestContext.Current.CancellationToken);

        f.TextToSpeech.SpokenText.Should().BeNull();
        f.ViewModel.ReadCurrentResponseStatus.Should().Contain("refused");
        f.Audit.Events.Should().HaveCount(audits);
    }

    [Fact]
    public async Task RetainedAnswerSourceWithdrawalAfterReadbackWithholdsOutputAndRecordsCancellation()
    {
        var f = await CreateReadableResponseAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var admitted = true;
        var readbacks = 0;
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var result = await f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request), () => admitted, _ =>
        {
            readbacks++;
            admitted = false;
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        readbacks.Should().Be(1);
        result.Should().NotContain("completed once");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
        f.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Cancelled);
    }

    [Fact]
    public async Task RetainedAnswerLostHostTraceAfterRequestedAuditCannotPlayOrClaimSuccess()
    {
        var f = await CreateReadableResponseAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        using var hostile = new Activity("untrusted-correlation");
        f.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            f.Audit.BeforeWrite = null;
            hostile.Start();
        };

        var result = await f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request), () => true,
            _ => throw new InvalidOperationException("A replaced trace cannot reach source readback."),
            TestContext.Current.CancellationToken);

        result.Should().NotContain("completed once");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
        f.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Failed);
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task RetainedAnswerEligibilityCallbackCannotReplaceTheHostTraceWithUntrustedCorrelation()
    {
        var f = await CreateReadableResponseAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        using var hostile = new Activity("untrusted-source-eligibility");
        var auditRequested = false;
        f.Audit.BeforeWrite = audit =>
        {
            if (string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                && audit.Outcome == SecurityAuditOutcome.Requested) { auditRequested = true; }
        };
        bool Admitted()
        {
            if (auditRequested)
            {
                auditRequested = false;
                hostile.Start();
            }
            return true;
        }

        var result = await f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request), Admitted,
            _ => throw new InvalidOperationException("Untrusted correlation cannot authorize retained source readback."),
            TestContext.Current.CancellationToken);

        result.Should().NotContain("completed once");
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
        f.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Cancelled);
    }

    [Fact]
    public async Task ResponseReadMissingHostSourceCannotAcquireAuthorityFromCallerTarget()
    {
        var f = await CreateReadableResponseAsync();
        f.ViewModel.RetireCurrentResponseSpeech();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);

        await ((ISpeechReadOncePlayback)f.ViewModel).ReadOnceAsync(new(Guid.NewGuid(), 1, f.ViewModel.CallPolicyRevision),
            SecurityAuditInitiator.LocalUser, TestContext.Current.CancellationToken);

        f.TextToSpeech.SpokenText.Should().BeNull();
        f.ViewModel.ReadCurrentResponseStatus.Should().Contain("refused");
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RetainedAnswerOpeningConsequentialDecisionRetiresActivePlaybackBeforeRebindingGate()
    {
        var f = await CreateReadableResponseAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        f.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        f.TextToSpeech.SpeakStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var read = f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request), () => true, _ => Task.CompletedTask,
            TestContext.Current.CancellationToken);
        await f.TextToSpeech.SpeakStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        f.ViewModel.BindRetainedAnswerReadDecisionGate(() => false);
        f.TextToSpeech.IsSpeaking.Should().BeFalse("the gate closes output synchronously, before later continuation");
        (await read).Should().NotContain("completed once");
        f.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.LocalUi)]
    public async Task RetainedAnswerRejectsNonOriginalOrInvokedOutputAuthority(RequestOrigin origin)
    {
        var f = await CreateReadableResponseAsync();
        var sourceRequest = HostRequest.Create(RequestOrigin.LocalUi);
        var input = new HostRequest(new(Guid.NewGuid()), sourceRequest.SessionId, new(Guid.NewGuid()), origin,
            origin == RequestOrigin.LocalUi ? new(Guid.NewGuid()) : null);
        using var activity = HostActivity.BeginRoot(input, HostActivityLayer.Application, HostOperation.Request);
        var validations = 0;
        var result = await f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(sourceRequest), () => true,
            _ => { validations++; return Task.CompletedTask; }, TestContext.Current.CancellationToken);

        result.Should().Contain("refused");
        validations.Should().Be(0);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Audit.Events.Should().NotContain(item => string.Equals(item.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RetainedAnswerQuestionReplyCaptureKeepsExclusiveOriginalInputAuthority()
    {
        var f = await ReplyHostAsync();
        using var host = new InteractionFixture();
        var question = await host.QuestionAsync();
        await using var replies = f.ViewModel.BindQuestionVoiceReplies(host.Questions, host.Time,
            NullLogger<Kora.Application.Interaction.HostQuestionVoiceReplyService>.Instance)!;
        await replies.OpenAsync(question, () => true, _ => { }, false, TestContext.Current.CancellationToken);
        var generation = f.Voice.Generation;
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);

        var result = await f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request), () => true,
            _ => throw new InvalidOperationException("No retained source may be read during question capture."),
            TestContext.Current.CancellationToken);

        result.Should().Contain("refused");
        replies.IsActive.Should().BeTrue();
        f.Voice.Generation.Should().Be(generation);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        replies.Retire("Test question closed without answering.");
        await replies.Quiescence;
    }

    [Theory]
    [InlineData("source")]
    [InlineData("policy")]
    public async Task RetainedAnswerRevalidatesTargetAfterRequestedAuditBeforeReadingSource(string retired)
    {
        var f = await CreateReadableResponseAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var admitted = true;
        var reads = 0;
        f.Audit.BeforeWrite = audit =>
        {
            if (!string.Equals(audit.ActionId, SpeechReadOnce.AuditActionId, StringComparison.Ordinal)
                || audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            if (retired is "source") { admitted = false; }
            else { f.CallState.SetState(Kora.Core.Communication.CallState.Unknown); }
        };
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var result = await f.ViewModel.ReadAnswerOnceAsync(SpeechAnswer(request), () => admitted,
            _ => { reads++; return Task.CompletedTask; }, TestContext.Current.CancellationToken);

        result.Should().NotContain("completed once");
        reads.Should().Be(0);
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.ViewModel.IsCurrentResponseReadActive.Should().BeFalse();
    }
}
