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

[Collection("Host tracing")]
public sealed class OrdinaryTurnDeadlineSuggestionReviewTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public OrdinaryTurnDeadlineSuggestionReviewTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    internal static ConversationMessage Answer(string text = "set work.active-deadline-minutes to 10") =>
        QuietHoursSuggestionReviewTests.Answer(text);
    internal static HostActivity Input(ConversationMessage message) => QuietHoursSuggestionReviewTests.Input(message);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("set work.active-deadline-minutes to 1", 1, false)]
    [InlineData("set work.active-deadline-minutes to 10", 10, true)]
    [InlineData("set work.active-deadline-minutes to 60", 60, false)]
    [InlineData("reset work.active-deadline-minutes", null, true)]
    [InlineData("reset work.active-deadline-minutes", null, false)]
    public async Task ExactRetainedAdviceHasNoWriteUntilSeparateNativeAuditedConfirmation(string text, int? minutes, bool saved)
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        if (saved) { f.Preferences.Save(new(7)); }
        var running = f.Service.Capture();
        var message = Answer(text);
        var validations = 0;
        OrdinaryTurnDeadlineSuggestionReview review;
        using (Input(message))
        {
            review = await OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(f.Service, f.CallPolicy, TimeProvider.System,
                message, () => true, _ => { validations++; return Task.CompletedTask; }, Token);
            review.Description.Should().Contain(saved ? "Current saved: 7 minutes" : "default five minutes (not saved)")
                .And.Contain("Proposed: " + (minutes is { } value ? $"{value} minutes" : "reset to default five minutes (not saved)"))
                .And.Contain("future ordinary task dispatch only").And.Contain("Running turns are unchanged")
                .And.Contain("fixed provider/tool ceilings").And.Contain("within 60 seconds");
            f.Audit.Events.Should().BeEmpty();
            f.Service.Capture().Should().Be(running);
            await FluentActions.Awaiting(() => review.ConfirmAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        using (review)
        using (Input(message))
        { (await review.ConfirmAsync(Token)).Should().BeTrue(); }
        var expected = minutes is { } number ? new OrdinaryTurnDeadline(number) : (OrdinaryTurnDeadline?)null;
        f.Preferences.Load().Should().Be(expected);
        f.Service.Capture().Should().Be(expected ?? OrdinaryTurnDeadline.Default);
        running.Should().Be(saved ? new OrdinaryTurnDeadline(7) : OrdinaryTurnDeadline.Default);
        f.Audit.Events.Select(item => (item.ActionId, item.Initiator, item.Outcome)).Should().Equal(
            ("configuration.ordinary-turn-deadline", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Requested),
            ("configuration.ordinary-turn-deadline", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Succeeded));
        validations.Should().Be(3);
        review.Available.Should().BeFalse();
        review.Description.Should().BeEmpty();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("source-return")]
    [InlineData("disk")]
    [InlineData("disk-return")]
    [InlineData("refresh")]
    [InlineData("expiry")]
    [InlineData("clock-back")]
    [InlineData("call")]
    [InlineData("call-return")]
    [InlineData("source-await")]
    [InlineData("cancel-await")]
    [InlineData("cancel-token")]
    public async Task StaleSourcePreferenceCallOrExpiryNeverWritesAndCannotRetry(string change)
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var time = new ReviewTime();
        var message = Answer();
        var live = true;
        var confirming = false;
        OrdinaryTurnDeadlineSuggestionReview? review = null;
        Task Validate(CancellationToken _)
        {
            if (confirming && change is "source-await") { live = false; }
            if (confirming && change is "cancel-await") { review!.Dispose(); }
            return Task.CompletedTask;
        }
        using (Input(message))
        { review = await OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(f.Service, f.CallPolicy, time, message, () => live, Validate, Token); }
        using (review)
        using (Input(message))
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token))
        {
            if (change is "cancel") { review.Dispose(); }
            if (change is "source" or "source-return") { live = false; }
            if (change is "source-return") { review.Available.Should().BeFalse(); live = true; }
            if (change is "disk" or "disk-return") { f.Preferences.Save(new(7)); }
            if (change is "disk-return") { review.Available.Should().BeFalse(); f.Preferences.Save(null); }
            if (change is "refresh") { await f.Refresh(); }
            if (change is "expiry") { time.Now = time.Now.AddSeconds(60); }
            if (change is "clock-back") { time.Now = time.Now.AddSeconds(-1); }
            if (change is "call" or "call-return")
            {
                f.CallPolicy.SetManual(true, RequestOrigin.LocalUi, f.CallPolicy.Current.Revision, () => true);
                if (change is "call-return")
                { f.CallPolicy.SetManual(false, RequestOrigin.LocalUi, f.CallPolicy.Current.Revision, () => true); }
            }
            if (change is "cancel-token") { cancellation.Cancel(); }
            confirming = true;
            await FluentActions.Awaiting(() => review.ConfirmAsync(cancellation.Token)).Should().ThrowAsync<Exception>();
            review.Available.Should().BeFalse();
            await FluentActions.Awaiting(() => review.ConfirmAsync(Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        f.Audit.Events.Should().BeEmpty();
        f.Preferences.Load().Should().Be(change is "disk" ? new OrdinaryTurnDeadline(7) : null);
    }

    [Theory]
    [InlineData("voice")]
    [InlineData("system")]
    [InlineData("foreign-session")]
    [InlineData("same-request")]
    [InlineData("invocation")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("source-voice")]
    [InlineData("other-setting")]
    [InlineData("malformed")]
    [InlineData("unsuccessful")]
    [InlineData("unconfirmed")]
    [InlineData("source-before")]
    [InlineData("source-first-await")]
    [InlineData("source-final-await")]
    [InlineData("cancelled")]
    [InlineData("clock-await")]
    [InlineData("refresh-await")]
    public async Task ReviewRefusesUntrustedOrChangedInputWithoutPreferenceWrite(string fault)
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var time = new ReviewTime();
        var message = Answer(fault is "other-setting" ? "set speech.playback-volume to 30" : "set work.active-deadline-minutes to 10");
        if (fault is "malformed") { message = message with { AssistantResponse = null }; }
        if (fault is "unsuccessful")
        { message = message with { Version = 1, Revision = 2, Outcome = ModelTurnOutcome.Denied, Execution = null, ExecutionMetadataRevision = null, AssistantResponse = null }; }
        if (fault is "source-voice")
        {
            var source = new HostRequest(message.Request.RequestId, message.Request.SessionId, message.Request.TaskId, RequestOrigin.ActivatedVoice);
            var execution = new ModelTurnProvenance(message.Execution!.Turn, source, message.Execution.Selection);
            message = message with { Request = source, Execution = execution, AssistantResponse = new(execution, message.AssistantResponse!.Body) };
        }
        if (fault is "unconfirmed") { f.Preferences.BeginWrite(); }
        var request = new HostRequest(new(Guid.NewGuid()), fault is "foreign-session" ? new(Guid.NewGuid()) : message.Request.SessionId,
            new(Guid.NewGuid()), fault is "voice" ? RequestOrigin.ActivatedVoice : fault is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (fault is "same-request") { request = message.Request; }
        if (fault is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using var input = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (fault is "stopped") { input.Activity!.Stop(); }
        if (fault is "completed") { input.Complete(HostOperationOutcome.Completed); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault is "cancelled") { cancellation.Cancel(); }
        var live = fault is not "source-before";
        var reads = 0;
        async Task Validate(CancellationToken _)
        {
            reads++;
            if (fault is "source-first-await" || fault is "source-final-await" && reads == 2) { live = false; }
            if (fault is "clock-await") { time.Now = time.Now.AddSeconds(60); }
            if (fault is "refresh-await" && reads == 2) { await f.Refresh(); }
        }
        await FluentActions.Awaiting(() => OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(f.Service, f.CallPolicy, time,
            message, () => live, Validate, cancellation.Token)).Should().ThrowAsync<Exception>();
        f.Audit.Events.Should().BeEmpty();
        f.Preferences.ReadBack().Should().BeNull();
    }

    [Theory]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("save")]
    [InlineData("readback")]
    [InlineData("terminal-receipt")]
    [InlineData("late-source")]
    public async Task SaveAndAuditUncertaintyNeverReportsConfirmedPreference(string fault)
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var message = Answer();
        var live = true;
        OrdinaryTurnDeadlineSuggestionReview review;
        using (Input(message))
        { review = await OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(f.Service, f.CallPolicy, TimeProvider.System, message,
            () => live, _ => Task.CompletedTask, Token); }
        f.Audit.Before = item =>
        {
            if (fault is "requested-audit" && item.Outcome == SecurityAuditOutcome.Requested
                || fault is "terminal-audit" && item.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("injected audit failure"); }
        };
        f.Store.Before = (operation, name) =>
        {
            if (fault is "save" && operation is "write" && name.Contains("minutes", StringComparison.Ordinal))
            { throw new IOException("injected write failure"); }
        };
        f.Store.After = (operation, name) =>
        {
            if (operation is "write" && name.Contains("minutes", StringComparison.Ordinal) && fault is "readback")
            { f.Store.Values[name] = "7"; }
        };
        f.Control.BeforeCommit = task => { if (fault is "late-source" && task.IsTerminal) { live = false; } };
        f.Control.FailTerminal = fault is "terminal-receipt";
        using (review)
        using (Input(message))
        { await FluentActions.Awaiting(() => review.ConfirmAsync(Token)).Should().ThrowAsync<Exception>(); }
        f.Store.Before = null;
        if (fault is "requested-audit") { f.Service.Capture().Should().Be(OrdinaryTurnDeadline.Default); }
        else { FluentActions.Invoking(() => f.Service.Capture()).Should().Throw<InvalidDataException>(); }
        review.Available.Should().BeFalse();
    }

    private sealed class ReviewTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task DisposedCallPolicyCannotAdmitNativeDeadlineReview()
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var message = Answer();
        f.CallPolicy.Dispose();
        using (Input(message))
        { await FluentActions.Awaiting(() => OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(f.Service, f.CallPolicy,
            TimeProvider.System, message, () => true, _ => Task.CompletedTask, Token)).Should().ThrowAsync<InvalidOperationException>(); }
        f.Preferences.Load().Should().BeNull();
        f.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task EligibilityRevokedDuringPolicyCheckPermanentlyRetiresDeadlineReview()
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var message = Answer();
        var revoke = false;
        var checks = 0;
        bool Eligible() => !revoke || ++checks == 1;
        OrdinaryTurnDeadlineSuggestionReview review;
        using (Input(message))
        { review = await OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(f.Service, f.CallPolicy, TimeProvider.System, message,
            Eligible, _ => Task.CompletedTask, Token); }
        using (review)
        {
            revoke = true;
            review.Available.Should().BeFalse();
            checks.Should().Be(2);
            revoke = false;
            review.Available.Should().BeFalse();
        }
        f.Preferences.Load().Should().BeNull();
        f.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposedCallPolicyRetiresAlreadyInspectedDeadlineReview()
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var message = Answer();
        OrdinaryTurnDeadlineSuggestionReview review;
        using (Input(message))
        { review = await OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(f.Service, f.CallPolicy, TimeProvider.System, message,
            () => true, _ => Task.CompletedTask, Token); }
        using (review)
        {
            f.CallPolicy.Dispose();
            review.Available.Should().BeFalse();
        }
        f.Preferences.Load().Should().BeNull();
        f.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task CallRevokedAtRequestedAuditReturnsDeniedWithoutPreferenceWrite()
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var message = Answer();
        OrdinaryTurnDeadlineSuggestionReview review;
        using (Input(message))
        { review = await OrdinaryTurnDeadlineSuggestionReview.ReviewAsync(f.Service, f.CallPolicy, TimeProvider.System, message,
            () => true, _ => Task.CompletedTask, Token); }
        f.Audit.Before = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested)
            { f.CallPolicy.SetManual(true, RequestOrigin.LocalUi, f.CallPolicy.Current.Revision, () => true); }
        };
        using (review)
        using (Input(message))
        { (await review.ConfirmAsync(Token)).Should().BeFalse(); }
        f.Preferences.Load().Should().BeNull();
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Denied);
    }
}
