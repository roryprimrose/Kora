using System.Diagnostics;

using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

public sealed partial class SpeechConfigurationServiceTests
{
    [Fact]
    public async Task RegisteredSpeechSuggestionRejectsStoppedParentAfterChildRestoresAmbientActivity()
    {
        using var f = new Fixture();
        var message = SpeechSuggestion();
        using var input = BeginSpeechInput(SpeechInput(message));
        using var child = new Activity("test.host-child").Start();
        input.Activity!.Stop();
        child.Stop();
        input.Activity.IsStopped.Should().BeTrue();
        HostActivity.RequireCurrent().Should().BeSameAs(input);
        await FluentActions.Awaiting(() => SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy,
            new SuggestionClock(), message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisteredSpeechSuggestionRejectsHostSystemSourceAndReusedOriginalRequest()
    {
        using var f = new Fixture();
        var system = SpeechSuggestion(origin: RequestOrigin.HostSystem);
        using (BeginSpeechInput(SpeechInput(system)))
        {
            await FluentActions.Awaiting(() => SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy,
                new SuggestionClock(), system, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidDataException>();
        }
        var message = SpeechSuggestion();
        SpeechSummarySuggestionReview review;
        using (BeginSpeechInput(SpeechInput(message)))
        {
            review = await SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy, new SuggestionClock(),
                message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        using (review)
        using (BeginSpeechInput(message.Request))
        {
            await FluentActions.Awaiting(() => review.ConfirmAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            review.Available.Should().BeTrue();
        }
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    private sealed class SuggestionClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ConversationMessage SpeechSuggestion(string body = "set speech.summary-words to 40",
        RequestOrigin origin = RequestOrigin.LocalUi)
    {
        var request = HostRequest.Create(origin);
        var execution = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "Suggest a limit for future ordinary speech.",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        {
            Version = 2, Execution = execution, ExecutionMetadataRevision = 0, AssistantResponse = new(execution, body),
        };
    }

    private static HostRequest SpeechInput(ConversationMessage message, RequestOrigin origin = RequestOrigin.LocalUi) =>
        new(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()), origin);
    private static HostActivity BeginSpeechInput(HostRequest request) =>
        HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);

    [Theory]
    [InlineData("set speech.summary-words to 1", 3, 1)]
    [InlineData("set speech.summary-words to 80", 3, 80)]
    [InlineData("set speech.summary-sentences to 1", 1, 80)]
    [InlineData("set speech.summary-sentences to 3", 3, 80)]
    [InlineData(" SET speech.summary-words to 40 ", 3, 40)]
    public async Task SpeechSuggestionUsesExistingBoundsAndCompleteTupleOnlyAfterFreshConfirmation(string text, int sentences, int words)
    {
        using var f = new Fixture();
        var message = SpeechSuggestion(text);
        var reads = 0;
        Task Validate(CancellationToken _) { reads++; return Task.CompletedTask; }
        SpeechSummarySuggestionReview review;
        using (var input = BeginSpeechInput(SpeechInput(message)))
        {
            review = await SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy, new SuggestionClock(), message,
                () => true, Validate, TestContext.Current.CancellationToken);
            review.Description.Should().Contain("Current: 3 sentences, 80 words (default)")
                .And.Contain($"Proposed: {sentences} sentences, {words} words")
                .And.Contain("companion cap is unchanged").And.Contain("shared device-local")
                .And.Contain("NEXT ordinary speech").And.Contain("not truncated, regenerated, spoken or replayed");
            f.Preferences.Writes.Should().Be(0);
            f.Audit.Events.Should().BeEmpty();
            await review.Invoking(item => item.ConfirmAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            review.Available.Should().BeTrue();
            input.Complete(HostOperationOutcome.Completed);
        }
        using (review)
        using (var input = BeginSpeechInput(SpeechInput(message)))
        {
            f.Audit.BeforeRequested = () =>
            {
                HostActivity.RequireCurrent().Request.Should().Be(input.Request);
                Activity.Current!.TraceId.Should().Be(input.Activity!.TraceId);
            };
            var result = await review.ConfirmAsync(TestContext.Current.CancellationToken, SecurityAuditInitiator.TypedCommand);
            result.Succeeded.Should().BeTrue();
            f.CreateService().Get().SummaryLimits.Should().Be(new SpokenSummaryLimits(sentences, words));
            f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            f.Audit.Events.Should().OnlyContain(item => item.Initiator == SecurityAuditInitiator.TypedCommand);
            review.Available.Should().BeFalse();
            review.Description.Should().BeEmpty();
            await review.Invoking(item => item.ConfirmAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        reads.Should().Be(2);
    }

    [Theory]
    [InlineData("set speech.summary-words to 0")]
    [InlineData("set speech.summary-words to 81")]
    [InlineData("set speech.summary-sentences to 0")]
    [InlineData("set speech.summary-sentences to 4")]
    [InlineData("set speech.summary-words to 040")]
    [InlineData("set speech.summary-words to +40")]
    [InlineData("set speech.summary-words to 40\nset speech.summary-sentences to 2")]
    [InlineData("set speech.summary-words to 40 and confirm")]
    [InlineData("set speech.provider to windows-sapi")]
    [InlineData("set speech.voice to default")]
    [InlineData("reset speech.summary-words")]
    [InlineData("get speech.summary-words")]
    [InlineData("set spoken summary word limit to 40")]
    [InlineData("Kora, set speech.summary-words to 40")]
    [InlineData("I applied speech.summary-words=40.")]
    [InlineData("set appearance.theme to dark")]
    public async Task SpeechSuggestionRejectsWholeMixedNoncanonicalAndUnadmittedAnswers(string text)
    {
        using var f = new Fixture();
        var message = SpeechSuggestion(text);
        using var input = BeginSpeechInput(SpeechInput(message));
        await FluentActions.Awaiting(() => SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy,
            new SuggestionClock(), message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.LocalUi, RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi, RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.ActivatedVoice, RequestOrigin.ActivatedVoice)]
    public async Task SpeechSuggestionPreservesOriginalVoiceAtAnyStageAndSavedCompanion(
        RequestOrigin source, RequestOrigin reviewOrigin, RequestOrigin confirmOrigin)
    {
        using var f = new Fixture();
        f.Preferences.Limits = new(2, 60);
        var message = SpeechSuggestion(origin: source);
        SpeechSummarySuggestionReview review;
        using (BeginSpeechInput(SpeechInput(message, reviewOrigin)))
        {
            review = await SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy, new SuggestionClock(),
                message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
            review.Description.Should().Contain("Current: 2 sentences, 60 words (saved)")
                .And.Contain("Proposed: 2 sentences, 40 words");
        }
        using (review)
        using (BeginSpeechInput(SpeechInput(message, confirmOrigin)))
        {
            (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Succeeded.Should().BeTrue();
            f.Preferences.Limits.Should().Be(new SpokenSummaryLimits(2, 40));
            var voice = source == RequestOrigin.ActivatedVoice || reviewOrigin == RequestOrigin.ActivatedVoice
                || confirmOrigin == RequestOrigin.ActivatedVoice;
            f.Audit.Events.Should().OnlyContain(item => item.Initiator ==
                (voice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser));
        }
    }

    [Theory]
    [InlineData("missing-context")]
    [InlineData("foreign-session")]
    [InlineData("invocation")]
    [InlineData("system")]
    [InlineData("same-request")]
    [InlineData("stopped")]
    [InlineData("stopped-activity")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("forged-provenance")]
    [InlineData("unreadable-settings")]
    [InlineData("source-before")]
    [InlineData("source-after")]
    [InlineData("settings-after")]
    [InlineData("voice-protected")]
    [InlineData("cancel-before")]
    public async Task SpeechSuggestionFailsClosedBeforeIssuingReview(string failure)
    {
        using var f = new Fixture();
        var message = SpeechSuggestion(origin: failure is "voice-protected" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        var request = SpeechInput(message);
        if (failure is "foreign-session") { request = HostRequest.Create(RequestOrigin.LocalUi); }
        if (failure is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        if (failure is "system") { request = new(request.RequestId, request.SessionId, request.TaskId, RequestOrigin.HostSystem); }
        if (failure is "same-request") { request = message.Request; }
        if (failure is "failed") { message = message with { Outcome = ModelTurnOutcome.Denied, AssistantResponse = null }; }
        if (failure is "cancelled") { message = message with { Outcome = ModelTurnOutcome.Cancelled, AssistantResponse = null }; }
        if (failure is "forged-provenance")
        { message = message with { AssistantResponse = message.AssistantResponse! with { Provenance = message.Execution! with { Request = SpeechInput(message) } } }; }
        if (failure is "unreadable-settings") { f.Preferences.LimitsLoadFailure = new InvalidDataException("broken"); }
        if (failure is "voice-protected")
        { f.Policy.SetManual(true, RequestOrigin.LocalUi, f.Policy.Current.Revision, () => true); }
        using var input = failure is "missing-context" ? null : BeginSpeechInput(request);
        if (failure is "stopped") { input!.Complete(HostOperationOutcome.Completed); }
        if (failure is "stopped-activity") { input!.Activity!.Stop(); Activity.Current = input.Activity; }
        var live = failure is not "source-before";
        Task Validate(CancellationToken _)
        {
            if (failure is "source-after") { live = false; }
            if (failure is "settings-after") { f.Preferences.Limits = new(1, 30); f.Service.Reload(); }
            return Task.CompletedTask;
        }
        using var cancel = new CancellationTokenSource();
        if (failure is "cancel-before") { cancel.Cancel(); }
        await FluentActions.Awaiting(() => SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy,
            new SuggestionClock(), message, () => live, Validate, cancel.Token)).Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("disposed")]
    [InlineData("source")]
    [InlineData("settings")]
    [InlineData("call")]
    [InlineData("late-source")]
    [InlineData("late-cancel")]
    [InlineData("source-read")]
    [InlineData("invalid-initiator")]
    public async Task SpeechSuggestionConfirmationConsumesOrRejectsStaleReviewWithoutWriting(string failure)
    {
        using var f = new Fixture();
        var message = SpeechSuggestion();
        var clock = new SuggestionClock();
        var live = true;
        var confirming = false;
        using var cancel = new CancellationTokenSource();
        Task Validate(CancellationToken _)
        {
            if (confirming && failure is "late-source") { live = false; }
            if (confirming && failure is "late-cancel") { cancel.Cancel(); }
            if (confirming && failure is "source-read") { throw new IOException("source revoked"); }
            return Task.CompletedTask;
        }
        SpeechSummarySuggestionReview review;
        using (BeginSpeechInput(SpeechInput(message)))
        {
            review = await SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy, clock, message,
                () => live, Validate, TestContext.Current.CancellationToken);
        }
        using (review)
        using (BeginSpeechInput(SpeechInput(message)))
        {
            confirming = true;
            if (failure is "expired") { clock.Now = clock.Now.AddSeconds(60); }
            if (failure is "disposed") { review.Dispose(); }
            if (failure is "source") { live = false; }
            if (failure is "settings") { f.Preferences.Limits = new(1, 20); f.Service.Reload(); }
            if (failure is "call") { f.Policy.SetManual(true, RequestOrigin.LocalUi, f.Policy.Current.Revision, () => true); }
            await review.Invoking(item => item.ConfirmAsync(cancel.Token, failure is "invalid-initiator"
                ? SecurityAuditInitiator.ModelSuggestion : SecurityAuditInitiator.LocalUser)).Should().ThrowAsync<Exception>();
            f.Preferences.Writes.Should().Be(0);
            f.Audit.Events.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("store")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("persisted-drift")]
    [InlineData("late-call")]
    public async Task SpeechSuggestionWriteFailuresHaveNoRetryAndNeverClaimRollback(string failure)
    {
        using var f = new Fixture();
        var message = SpeechSuggestion();
        SpeechSummarySuggestionReview review;
        using (BeginSpeechInput(SpeechInput(message)))
        {
            review = await SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy, new SuggestionClock(),
                message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        using (review)
        using (BeginSpeechInput(SpeechInput(message)))
        {
            if (failure is "store") { f.Preferences.Failure = new IOException("disk"); }
            if (failure is "requested-audit") { f.Audit.FailureOutcome = SecurityAuditOutcome.Requested; }
            if (failure is "terminal-audit") { f.Audit.FailureOutcome = SecurityAuditOutcome.Succeeded; }
            if (failure is "persisted-drift") { f.Preferences.Limits = new(1, 50); }
            if (failure is "late-call")
            { f.Audit.BeforeRequested = () => f.Policy.SetManual(true, RequestOrigin.LocalUi, f.Policy.Current.Revision, () => true); }
            if (failure.EndsWith("audit", StringComparison.Ordinal))
            {
                await review.Invoking(item => item.ConfirmAsync(TestContext.Current.CancellationToken))
                    .Should().ThrowAsync<InvalidOperationException>();
            }
            else
            {
                (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Succeeded.Should().BeFalse();
            }
            f.Preferences.Writes.Should().Be(failure is "terminal-audit" ? 1 : 0);
            review.Available.Should().BeFalse();
            await review.Invoking(item => item.ConfirmAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Theory]
    [InlineData("review speech limit suggestion", SpeechSummarySuggestionOperation.Review)]
    [InlineData("Nova, confirm speech limit change", SpeechSummarySuggestionOperation.Confirm)]
    [InlineData("Nova cancel speech limit review", SpeechSummarySuggestionOperation.Cancel)]
    [InlineData("yes", null)]
    [InlineData("Nova", null)]
    [InlineData("Novax, confirm speech limit change", null)]
    [InlineData("confirm speech limit change and apply", null)]
    public void SpeechSuggestionExactPhrasesDoNotCollideOrAcceptGenericApproval(string text, SpeechSummarySuggestionOperation? expected)
    {
        SpeechSummarySuggestionCommand.Parse(text, "Nova").Should().Be(expected);
        foreach (var phrase in SpeechSummarySuggestionCommand.FixedPhrases)
        {
            SpeechCommand.Parse(phrase, "Nova").Should().BeNull();
            AppearanceCommand.Parse(phrase, "Nova").Should().BeNull();
        }
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice)]
    public async Task SpeechSuggestionTraceLinksDeferredReviewAndAdmittedSourceWithoutUserTextOrIncomingAuthority(
        RequestOrigin originalOrigin, RequestOrigin reviewOrigin)
    {
        using var f = new Fixture();
        var policies = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("kora.source.request.id") is not null) { policies.Add(activity); }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var message = SpeechSuggestion(origin: originalOrigin);
        SpeechSummarySuggestionReview review;
        ActivityContext reviewed;
        using (var input = BeginSpeechInput(SpeechInput(message, reviewOrigin)))
        {
            reviewed = input.Activity!.Context;
            review = await SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy, new SuggestionClock(),
                message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        using (review)
        using (var hostile = new Activity("untrusted incoming correlation").Start())
        using (var input = BeginSpeechInput(SpeechInput(message)))
        {
            (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Succeeded.Should().BeTrue();
            var policy = policies.Single();
            policy.TraceId.Should().Be(input.Activity!.TraceId).And.NotBe(hostile.TraceId);
            policy.ParentSpanId.Should().Be(input.Activity.SpanId);
            policy.Links.Single().Context.Should().Be(reviewed);
            policy.GetTagItem("kora.source.request.id").Should().Be(message.Request.RequestId.Value);
            policy.GetTagItem("kora.source.task.id").Should().Be(message.Request.TaskId.Value);
            policy.GetTagItem("kora.source.turn.id").Should().Be(message.Execution!.Turn.Value);
            policy.GetTagItem("kora.source.origin").Should().Be(originalOrigin.ToString());
            policy.Status.Should().Be(ActivityStatusCode.Ok);
            policy.TagObjects.Should().NotContain(tag => Equals(tag.Value, message.Body)
                || Equals(tag.Value, message.AssistantResponse!.Body));
            policy.Baggage.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SpeechSuggestionWithoutInstalledSpeechKeepsVisualReviewAndSavedCapsWithoutSubstitution()
    {
        using var f = new Fixture();
        f.Catalog.Providers = [];
        var message = SpeechSuggestion();
        SpeechSummarySuggestionReview review;
        using (BeginSpeechInput(SpeechInput(message)))
        {
            review = await SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy, new SuggestionClock(),
                message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
            review.Description.Should().Contain("unavailable").And.Contain("Nothing is downloaded or substituted");
        }
        using (review)
        using (BeginSpeechInput(SpeechInput(message)))
        {
            var result = await review.ConfirmAsync(TestContext.Current.CancellationToken);
            result.Succeeded.Should().BeTrue();
            result.State.IsAvailable.Should().BeFalse();
            f.Preferences.Limits.Should().Be(new SpokenSummaryLimits(3, 40));
            f.Preferences.Selection.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpeechSuggestionAuditCallbackCannotRetireOrReplaceOriginalConfirmationActivityAndWrite(bool stop)
    {
        using var f = new Fixture();
        var message = SpeechSuggestion();
        SpeechSummarySuggestionReview review;
        using (BeginSpeechInput(SpeechInput(message)))
        {
            review = await SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy, new SuggestionClock(),
                message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        using (review)
        using (BeginSpeechInput(SpeechInput(message)))
        {
            f.Audit.BeforeRequested = () =>
            {
                var activity = HostActivity.RequireCurrent();
                if (stop) { activity.Activity!.Stop(); Activity.Current = activity.Activity; }
                else { activity.Complete(HostOperationOutcome.Completed); }
            };
            (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Succeeded.Should().BeFalse();
            f.Preferences.Writes.Should().Be(0);
            f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        }
    }

    [Fact]
    public async Task SpeechSuggestionStoppedHostContextCannotBeRestoredAsOriginalUserAuthority()
    {
        using var f = new Fixture();
        var message = SpeechSuggestion();
        using var input = BeginSpeechInput(SpeechInput(message));
        input.Activity!.Stop();
        Activity.Current = input.Activity;
        FluentActions.Invoking(HostActivity.RequireCurrent).Should().Throw<InvalidOperationException>();
        await FluentActions.Awaiting(() => SpeechSummarySuggestionReview.ReviewAsync(f.Service, f.Policy,
            new SuggestionClock(), message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }
}
