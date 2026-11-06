using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class DetailPresentationSuggestionReviewTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public DetailPresentationSuggestionReviewTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData("set responses.detail-presentation to Offer", DetailPresentationMode.Offer, false)]
    [InlineData("set responses.detail-presentation to LinkOnly", DetailPresentationMode.LinkOnly, true)]
    [InlineData("set responses.detail-presentation to OpenAutomatically", DetailPresentationMode.OpenAutomatically, true)]
    [InlineData("reset responses.detail-presentation", DetailPresentationMode.Offer, true)]
    public async Task OriginalAdviceReviewsExactSavedValueThenSeparatelyUsesNativeAuditAndConfirmedReadback(
        string answer, DetailPresentationMode expected, bool saved)
    {
        await using var f = new DetailPresentationConfigurationServiceTests.Fixture();
        f.Preferences.Value = saved ? DetailPresentationMode.LinkOnly : null;
        var message = Answer(answer);
        var validations = 0;
        DetailPresentationSuggestionReview review;
        using (Input(message))
        {
            review = await DetailPresentationSuggestionReview.ReviewAsync(f.Service, f.Policy, f.Time, message,
                () => true, _ => { validations++; return Task.CompletedTask; }, f.Token);
            review.Available.Should().BeTrue();
            review.Description.Should().Contain(saved ? "LinkOnly -> " + expected : "Offer (unsaved default) -> " + expected)
                .And.Contain("future publications").And.Contain("Confirm separately").And.Contain("Nothing has changed");
            f.Preferences.Writes.Should().Be(0);
            f.Audit.Events.Should().BeEmpty();
            await FluentActions.Awaiting(() => review.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        using (review)
        using (Input(message))
        { (await review.ConfirmAsync(f.Token)).Should().BeTrue(); }
        f.Preferences.Value.Should().Be(expected);
        f.Preferences.Pending.Should().BeFalse();
        var restarted = f.Create();
        restarted.Observe();
        restarted.Description.Should().Contain(expected + " (saved)");
        f.Audit.Events.Select(item => (item.ActionId, item.Initiator, item.Outcome)).Should().Equal(
            ("configuration.detail-presentation", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Requested),
            ("configuration.detail-presentation", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Succeeded));
        validations.Should().Be(3);
        review.Available.Should().BeFalse();
        review.Description.Should().BeEmpty();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("dispose")]
    [InlineData("expired")]
    [InlineData("clock")]
    [InlineData("call")]
    [InlineData("unknown-call")]
    [InlineData("source-await")]
    [InlineData("cancel-await")]
    [InlineData("revision")]
    [InlineData("native-review")]
    [InlineData("saved")]
    [InlineData("write")]
    [InlineData("readback")]
    [InlineData("audit")]
    [InlineData("cancel-token")]
    public async Task StaleSourceNativeReviewOrUnconfirmedWriteNeverBecomesSuccessfulConfirmation(string transition)
    {
        await using var f = new DetailPresentationConfigurationServiceTests.Fixture();
        var message = Answer();
        var live = true;
        var confirming = false;
        DetailPresentationSuggestionReview? review = null;
        Task Validate(CancellationToken _)
        {
            if (confirming && transition is "source-await") { live = false; }
            if (confirming && transition is "cancel-await") { review!.Dispose(); }
            return Task.CompletedTask;
        }
        using (Input(message))
        { review = await DetailPresentationSuggestionReview.ReviewAsync(f.Service, f.Policy, f.Time, message, () => live, Validate, f.Token); }
        using (review)
        using (Input(message))
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token))
        {
            switch (transition)
            {
                case "source": live = false; break;
                case "dispose": review.Dispose(); break;
                case "expired": f.Time.Now = f.Time.Now.AddSeconds(60); break;
                case "clock": f.Time.Now = f.Time.Now.AddSeconds(-1); break;
                case "call": f.Call.State = CallState.Active; break;
                case "unknown-call": f.Call.State = CallState.Unknown; break;
                case "revision": f.Service.HoldUnavailable(); f.Service.Observe(); break;
                case "native-review": await f.Review(DetailPresentationMode.Offer); break;
                case "saved": f.Preferences.Value = DetailPresentationMode.Offer; break;
                case "write": f.Preferences.SaveFailure = new IOException("injected write"); break;
                case "readback": f.Preferences.AfterSave = () => f.Preferences.Value = DetailPresentationMode.Offer; break;
                case "audit": f.Audit.BeforeWrite = _ => throw new IOException("injected audit"); break;
                case "cancel-token": cancellation.Cancel(); break;
            }
            confirming = true;
            await FluentActions.Awaiting(() => review.ConfirmAsync(cancellation.Token)).Should().ThrowAsync<Exception>();
        }
        if (transition is not "readback") { f.Preferences.Writes.Should().Be(0); }
        f.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("voice")]
    [InlineData("system")]
    [InlineData("foreign-session")]
    [InlineData("same-request")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("invocation")]
    [InlineData("other-setting")]
    [InlineData("unsuccessful")]
    [InlineData("missing-answer")]
    [InlineData("source-before")]
    [InlineData("source-await")]
    [InlineData("source-late")]
    [InlineData("cancelled")]
    [InlineData("cancel-await")]
    [InlineData("load")]
    public async Task ReviewRejectsUntrustedOrChangedInputWithoutWrite(string boundary)
    {
        await using var f = new DetailPresentationConfigurationServiceTests.Fixture();
        var message = boundary is "other-setting" ? Answer("set responses.default-mode to VisualOnly") : Answer();
        if (boundary is "missing-answer") { message = message with { AssistantResponse = null }; }
        if (boundary is "unsuccessful")
        {
            message = message with { Version = 1, Revision = 2, Outcome = ModelTurnOutcome.Denied, Execution = null,
                ExecutionMetadataRevision = null, AssistantResponse = null };
        }
        var request = new HostRequest(new(Guid.NewGuid()),
            boundary is "foreign-session" ? new(Guid.NewGuid()) : message.Request.SessionId, new(Guid.NewGuid()),
            boundary switch { "voice" => RequestOrigin.ActivatedVoice, "system" => RequestOrigin.HostSystem, _ => RequestOrigin.LocalUi },
            boundary is "invocation" ? new HostId<InvocationIdentity>(Guid.NewGuid()) : null);
        if (boundary is "same-request") { request = message.Request; }
        using var input = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        using var child = boundary is "stopped" ? new Activity("test.host-child").Start() : null;
        if (boundary is "stopped") { input.Activity!.Stop(); child!.Stop(); }
        if (boundary is "completed") { input.Complete(HostOperationOutcome.Completed); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (boundary is "cancelled") { cancellation.Cancel(); }
        if (boundary is "load") { f.Preferences.LoadFailure = new InvalidDataException("injected load"); }
        var live = boundary is not "source-before";
        var reads = 0;
        Task Validate(CancellationToken _)
        {
            reads++;
            if (boundary is "source-await" || boundary is "source-late" && reads == 2) { live = false; }
            if (boundary is "cancel-await") { cancellation.Cancel(); }
            return Task.CompletedTask;
        }
        await FluentActions.Awaiting(() => DetailPresentationSuggestionReview.ReviewAsync(f.Service, f.Policy, f.Time,
            message, () => live, Validate, cancellation.Token)).Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task OriginalCallPolicyDenialDuringRequestedAuditCannotWriteOrReportSuccess()
    {
        await using var f = new DetailPresentationConfigurationServiceTests.Fixture();
        var message = Answer();
        DetailPresentationSuggestionReview review;
        using (Input(message))
        { review = await DetailPresentationSuggestionReview.ReviewAsync(f.Service, f.Policy, f.Time, message, () => true, _ => Task.CompletedTask, f.Token); }
        f.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested) { f.Call.State = CallState.Unknown; }
        };
        using (review)
        using (Input(message))
        { (await review.ConfirmAsync(f.Token)).Should().BeFalse(); }
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Denied);
        review.Available.Should().BeFalse();
    }

    [Fact]
    public async Task RetiringAdviceDoesNotCancelNewerNativeSettingsReview()
    {
        await using var f = new DetailPresentationConfigurationServiceTests.Fixture();
        var message = Answer();
        DetailPresentationSuggestionReview review;
        using (Input(message))
        { review = await DetailPresentationSuggestionReview.ReviewAsync(f.Service, f.Policy, f.Time, message, () => true, _ => Task.CompletedTask, f.Token); }
        using (Input(message))
        {
            var native = await f.Review(DetailPresentationMode.Offer);
            review.Dispose();
            (await f.Confirm(native)).Should().BeTrue();
        }
        f.Preferences.Value.Should().Be(DetailPresentationMode.Offer);
    }

    internal static ConversationMessage Answer(string answer = "set responses.detail-presentation to LinkOnly") =>
        QuietHoursSuggestionReviewTests.Answer(answer);
    internal static HostActivity Input(ConversationMessage message) => QuietHoursSuggestionReviewTests.Input(message);
}
