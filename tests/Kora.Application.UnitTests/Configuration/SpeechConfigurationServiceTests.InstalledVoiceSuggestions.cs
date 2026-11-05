using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed partial class SpeechConfigurationServiceTests
{
    [Fact]
    public async Task InstalledVoiceReviewRejectsStoppedParentRestoredByChildWithoutTreatingAmbientContextAsAuthority()
    {
        using var f = new Fixture();
        var message = SpeechSuggestion("set speech.voice to kokoro / second");
        using var input = BeginSpeechInput(SpeechInput(message));
        using var child = new Activity("test.host-child").Start();
        input.Activity!.Stop();
        child.Stop();
        input.Activity.IsStopped.Should().BeTrue();
        HostActivity.RequireCurrent().Should().BeSameAs(input);
        await FluentActions.Awaiting(() => InstalledSpeechVoiceSuggestionReview.ReviewAsync(f.Service, f.Policy,
            new SuggestionClock(), message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task InstalledVoiceReviewBindsExactPairAndDistinctInputWithFullIdAndNoProviderDefault(bool maximumId, bool saved)
    {
        using var f = new Fixture();
        var id = maximumId ? new string('v', SpeechSelection.MaximumVoiceIdLength) : "default";
        f.Catalog.Providers = f.Catalog.Providers.Select(item => string.Equals(item.Id, SpeechProviderIds.Kokoro
, StringComparison.Ordinal) ? item with { DefaultVoiceId = null } : item).ToArray();
        var voice = new SpeechVoice(id, "Installed voice", "en-US", SpeechVoiceGender.Neutral) { ProviderId = SpeechProviderIds.Kokoro };
        f.Catalog.Voices = f.Catalog.Voices.Where(item => !string.Equals(item.ProviderId, SpeechProviderIds.Kokoro, StringComparison.Ordinal)).Append(voice).ToArray();
        if (saved) { f.Preferences.Selection = new(SpeechProviderIds.Windows, "default"); }
        var message = SpeechSuggestion($"set speech.voice to {voice.ConfigurationId}");
        var reads = 0;
        Task Validate(CancellationToken _) { reads++; return Task.CompletedTask; }
        InstalledSpeechVoiceSuggestionReview review;
        using (BeginSpeechInput(SpeechInput(message)))
        {
            review = await InstalledSpeechVoiceSuggestionReview.ReviewAsync(f.Service, f.Policy, new SuggestionClock(),
                message, () => true, Validate, TestContext.Current.CancellationToken);
            review.Description.Should().Contain("Current provider: windows-sapi; voice:")
                .And.Contain(saved ? "(saved)" : "(default)")
                .And.Contain($"Proposed provider: kokoro; voice: {id}")
                .And.Contain("exact successful retained answer").And.Contain("shared device-local")
                .And.Contain("NEXT separately requested eligible ordinary speech")
                .And.Contain("never synthesized, regenerated or replayed");
            await review.Invoking(item => item.ConfirmAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
            review.Available.Should().BeTrue();
            f.Preferences.Writes.Should().Be(0);
            f.Audit.Events.Should().BeEmpty();
        }
        using (review)
        using (var confirmation = BeginSpeechInput(SpeechInput(message)))
        {
            f.Audit.BeforeRequested = () =>
            {
                HostActivity.RequireCurrent().Request.Should().Be(confirmation.Request);
                Activity.Current!.TraceId.Should().Be(confirmation.Activity!.TraceId);
            };
            var result = await review.ConfirmAsync(TestContext.Current.CancellationToken, SecurityAuditInitiator.TypedCommand);
            result.Succeeded.Should().BeTrue();
            var expected = new SpeechSelection(SpeechProviderIds.Kokoro, id);
            result.State.Selection.Should().Be(expected);
            result.State.EffectiveVoice.Should().Be(voice);
            f.CreateService().Get().Selection.Should().Be(expected);
            f.Preferences.Writes.Should().Be(1);
            f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            f.Audit.Events.Should().OnlyContain(item => item.ActionId == SpeechOptionRegistry.Get(SpeechOption.Voice).AuditAction
                && item.Initiator == SecurityAuditInitiator.TypedCommand && item.TargetId == "preferences.device-local");
            review.Available.Should().BeFalse();
            review.Description.Should().BeEmpty();
            await review.Invoking(item => item.ConfirmAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        reads.Should().Be(2);
    }

    [Theory]
    [InlineData("set speech.voice to default")]
    [InlineData("set speech.voice to kokoro / missing")]
    [InlineData("set speech.voice to unknown / second")]
    [InlineData("set speech.voice to kokoro / second and confirm")]
    [InlineData("set speech.voice to kokoro / second; set speech.provider to windows-sapi")]
    [InlineData("set speech.voice to kokoro / second\nDone.")]
    [InlineData("set speech.provider to kokoro")]
    [InlineData("set speech.summary-words to 40")]
    [InlineData("reset speech.voice")]
    public async Task InstalledVoiceRejectsUnknownAmbiguousCommentaryAndOtherFamiliesWithoutEffects(string text)
    {
        using var f = new Fixture();
        var message = SpeechSuggestion(text);
        using var input = BeginSpeechInput(SpeechInput(message));
        await FluentActions.Awaiting(() => InstalledSpeechVoiceSuggestionReview.ReviewAsync(f.Service, f.Policy,
            new SuggestionClock(), message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("misresolved")]
    [InlineData("provider-removed")]
    [InlineData("assets-missing")]
    [InlineData("invalid-settings")]
    [InlineData("stale-settings")]
    [InlineData("catalogue-failure")]
    [InlineData("source-before")]
    [InlineData("source-after")]
    [InlineData("catalogue-after")]
    [InlineData("input-after")]
    [InlineData("voice-protected")]
    [InlineData("foreign-session")]
    [InlineData("invocation")]
    [InlineData("system")]
    [InlineData("same-request")]
    [InlineData("stopped")]
    [InlineData("stopped-activity")]
    [InlineData("failed-source")]
    [InlineData("cancelled")]
    public async Task InstalledVoiceReviewFailsClosedOnSourceAuthorityAndSnapshotLoss(string loss)
    {
        using var f = new Fixture();
        var message = SpeechSuggestion("set speech.voice to kokoro / second",
string.Equals(loss, "voice-protected", StringComparison.Ordinal) ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        if (string.Equals(loss, "failed-source", StringComparison.Ordinal)) { message = message with { Outcome = ModelTurnOutcome.Denied, AssistantResponse = null }; }
        if (string.Equals(loss, "duplicate", StringComparison.Ordinal)) { f.Catalog.Voices = f.Catalog.Voices.Append(f.Catalog.Voices[2]).ToArray(); }
        if (string.Equals(loss, "misresolved", StringComparison.Ordinal))
        { f.Catalog.Voices = [new("kokoro / second", "Other ID", "en-US", SpeechVoiceGender.Neutral)]; }
        if (string.Equals(loss, "provider-removed", StringComparison.Ordinal)) { f.Catalog.Providers = f.Catalog.Providers.Where(item => !string.Equals(item.Id, SpeechProviderIds.Kokoro, StringComparison.Ordinal)).ToArray(); }
        if (string.Equals(loss, "assets-missing", StringComparison.Ordinal)) { f.Catalog.Providers = f.Catalog.Providers.Select(item => item with { IsInstalled = false }).ToArray(); }
        if (string.Equals(loss, "invalid-settings", StringComparison.Ordinal)) { f.Preferences.LoadFailure = new InvalidDataException("invalid"); }
        if (string.Equals(loss, "catalogue-failure", StringComparison.Ordinal)) { f.Catalog.Failure = new IOException("unavailable"); }
        if (string.Equals(loss, "stale-settings", StringComparison.Ordinal)) { _ = f.Service.Get(); f.Preferences.Selection = new(SpeechProviderIds.Windows, "default"); }
        if (string.Equals(loss, "voice-protected", StringComparison.Ordinal)) { f.Policy.SetManual(true, RequestOrigin.LocalUi, f.Policy.Current.Revision, () => true); }
        var request = string.Equals(loss, "foreign-session", StringComparison.Ordinal) ? HostRequest.Create(RequestOrigin.LocalUi)
            : string.Equals(loss, "same-request", StringComparison.Ordinal) ? message.Request : SpeechInput(message);
        if (string.Equals(loss, "invocation", StringComparison.Ordinal)) { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        if (string.Equals(loss, "system", StringComparison.Ordinal)) { request = new(request.RequestId, request.SessionId, request.TaskId, RequestOrigin.HostSystem); }
        using var input = BeginSpeechInput(request);
        if (string.Equals(loss, "stopped", StringComparison.Ordinal)) { input.Complete(HostOperationOutcome.Completed); }
        if (string.Equals(loss, "stopped-activity", StringComparison.Ordinal))
        {
            input.Activity!.Stop();
            Activity.Current = input.Activity;
        }
        var live = !string.Equals(loss, "source-before", StringComparison.Ordinal);
        Task Validate(CancellationToken _)
        {
            if (string.Equals(loss, "source-after", StringComparison.Ordinal)) { live = false; }
            if (string.Equals(loss, "catalogue-after", StringComparison.Ordinal)) { f.Catalog.Voices = []; }
            if (string.Equals(loss, "input-after", StringComparison.Ordinal)) { input.Complete(HostOperationOutcome.Completed); }
            return Task.CompletedTask;
        }
        using var cancellation = new CancellationTokenSource();
        if (string.Equals(loss, "cancelled", StringComparison.Ordinal)) { cancellation.Cancel(); }
        await FluentActions.Awaiting(() => InstalledSpeechVoiceSuggestionReview.ReviewAsync(f.Service, f.Policy,
            new SuggestionClock(), message, () => live, Validate, cancellation.Token)).Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("disposed")]
    [InlineData("source")]
    [InlineData("late-source")]
    [InlineData("source-read")]
    [InlineData("settings")]
    [InlineData("persisted-settings")]
    [InlineData("provider")]
    [InlineData("voice")]
    [InlineData("default")]
    [InlineData("readiness")]
    [InlineData("call")]
    [InlineData("late-cancel")]
    [InlineData("late-dispose")]
    [InlineData("same-source")]
    [InlineData("foreign-session")]
    [InlineData("invalid-initiator")]
    public async Task InstalledVoiceConfirmationRevalidatesUnnotifiedCatalogueAndOriginalSourceWithoutWrites(string loss)
    {
        using var f = new Fixture();
        var message = SpeechSuggestion("set speech.voice to kokoro / second", RequestOrigin.ActivatedVoice);
        var time = new SuggestionClock();
        var live = true;
        var confirming = false;
        InstalledSpeechVoiceSuggestionReview? pending = null;
        using var cancellation = new CancellationTokenSource();
        Task Validate(CancellationToken _)
        {
            if (confirming && string.Equals(loss, "late-source", StringComparison.Ordinal)) { live = false; }
            if (confirming && string.Equals(loss, "source-read", StringComparison.Ordinal)) { throw new IOException("retired source"); }
            if (confirming && string.Equals(loss, "late-cancel", StringComparison.Ordinal)) { cancellation.Cancel(); }
            if (confirming && string.Equals(loss, "late-dispose", StringComparison.Ordinal)) { pending!.Dispose(); }
            return Task.CompletedTask;
        }
        InstalledSpeechVoiceSuggestionReview review;
        using (BeginSpeechInput(SpeechInput(message)))
        {
            review = await InstalledSpeechVoiceSuggestionReview.ReviewAsync(f.Service, f.Policy, time, message,
                () => live, Validate, TestContext.Current.CancellationToken);
            pending = review;
        }
        using (review)
        using (BeginSpeechInput(string.Equals(loss, "foreign-session", StringComparison.Ordinal) ? HostRequest.Create(RequestOrigin.LocalUi)
            : string.Equals(loss, "same-source", StringComparison.Ordinal) ? message.Request : SpeechInput(message)))
        {
            confirming = true;
            if (string.Equals(loss, "expired", StringComparison.Ordinal)) { time.Now = time.Now.AddSeconds(60); }
            if (string.Equals(loss, "disposed", StringComparison.Ordinal)) { review.Dispose(); }
            if (string.Equals(loss, "source", StringComparison.Ordinal)) { live = false; }
            if (loss is "settings" or "persisted-settings")
            {
                f.Preferences.Selection = new(SpeechProviderIds.Windows, "default");
                if (string.Equals(loss, "settings", StringComparison.Ordinal)) { f.Service.Reload(); }
            }
            if (string.Equals(loss, "provider", StringComparison.Ordinal)) { f.Catalog.Providers = f.Catalog.Providers.Take(1).ToArray(); }
            if (string.Equals(loss, "voice", StringComparison.Ordinal)) { f.Catalog.Voices = f.Catalog.Voices.Take(2).ToArray(); }
            if (string.Equals(loss, "default", StringComparison.Ordinal)) { f.Catalog.Providers = f.Catalog.Providers.Select(item => item with { DefaultVoiceId = null }).ToArray(); }
            if (string.Equals(loss, "readiness", StringComparison.Ordinal)) { f.Catalog.Providers = f.Catalog.Providers.Select(item => item with { IsInstalled = false }).ToArray(); }
            if (string.Equals(loss, "call", StringComparison.Ordinal)) { f.Policy.SetManual(true, RequestOrigin.LocalUi, f.Policy.Current.Revision, () => true); }
            await review.Invoking(item => item.ConfirmAsync(cancellation.Token, string.Equals(loss, "invalid-initiator"
, StringComparison.Ordinal) ? SecurityAuditInitiator.ModelSuggestion : SecurityAuditInitiator.LocalUser)).Should().ThrowAsync<Exception>();
            f.Preferences.Writes.Should().Be(0);
            f.Audit.Events.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("write")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("readback")]
    [InlineData("late-call")]
    [InlineData("late-catalogue")]
    [InlineData("late-source")]
    public async Task InstalledVoiceCommitFailureHasNoBridgeSuccessOrRetryAndNeverClaimsRollback(string loss)
    {
        using var f = new Fixture();
        var message = SpeechSuggestion("set speech.voice to kokoro / second", RequestOrigin.ActivatedVoice);
        var live = true;
        InstalledSpeechVoiceSuggestionReview review;
        using (BeginSpeechInput(SpeechInput(message)))
        {
            review = await InstalledSpeechVoiceSuggestionReview.ReviewAsync(f.Service, f.Policy, new SuggestionClock(),
                message, () => live, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        using (review)
        using (BeginSpeechInput(SpeechInput(message)))
        {
            if (string.Equals(loss, "write", StringComparison.Ordinal)) { f.Preferences.Failure = new IOException("disk"); }
            if (string.Equals(loss, "requested-audit", StringComparison.Ordinal)) { f.Audit.FailureOutcome = SecurityAuditOutcome.Requested; }
            if (string.Equals(loss, "terminal-audit", StringComparison.Ordinal)) { f.Audit.FailureOutcome = SecurityAuditOutcome.Succeeded; }
            if (string.Equals(loss, "readback", StringComparison.Ordinal)) { f.Preferences.AfterWrite = () => f.Preferences.Selection = SpeechSelection.Default; }
            f.Audit.BeforeRequested = () =>
            {
                if (string.Equals(loss, "late-call", StringComparison.Ordinal)) { f.Policy.SetManual(true, RequestOrigin.LocalUi, f.Policy.Current.Revision, () => true); }
                if (string.Equals(loss, "late-catalogue", StringComparison.Ordinal)) { f.Catalog.Voices = []; }
                if (string.Equals(loss, "late-source", StringComparison.Ordinal)) { live = false; }
            };
            if (loss.EndsWith("audit", StringComparison.Ordinal))
            { await review.Invoking(item => item.ConfirmAsync(TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>(); }
            else
            { (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Succeeded.Should().BeFalse(); }
            f.Preferences.Writes.Should().Be(loss is "terminal-audit" or "readback" ? 1 : 0);
            f.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
            review.Available.Should().BeFalse();
            await review.Invoking(item => item.ConfirmAsync(TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task InstalledVoiceReviewShowsUnavailableCurrentSelectionAndExplicitRecoveryWithoutSubstitution()
    {
        using var f = new Fixture();
        f.Preferences.Selection = new(SpeechProviderIds.Windows, "missing");
        var message = SpeechSuggestion("set speech.voice to kokoro / second");
        using var input = BeginSpeechInput(SpeechInput(message));
        using var review = await InstalledSpeechVoiceSuggestionReview.ReviewAsync(f.Service, f.Policy, new SuggestionClock(),
            message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        review.Description.Should().Contain("effective: unavailable").And.Contain("selected provider or voice is unavailable")
            .And.Contain("Proposed provider: kokoro; voice: second");
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task InstalledVoiceNewNativeSourceRemainsSubjectToExistingProtectedCallGate()
    {
        using var f = new Fixture();
        f.Policy.SetManual(true, RequestOrigin.LocalUi, f.Policy.Current.Revision, () => true);
        var message = SpeechSuggestion("set speech.voice to kokoro / second");
        InstalledSpeechVoiceSuggestionReview review;
        using (BeginSpeechInput(SpeechInput(message)))
        {
            review = await InstalledSpeechVoiceSuggestionReview.ReviewAsync(f.Service, f.Policy, new SuggestionClock(),
                message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        using (review)
        using (BeginSpeechInput(SpeechInput(message)))
        {
            (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Succeeded.Should().BeTrue();
            f.Preferences.Selection.Should().Be(new SpeechSelection(SpeechProviderIds.Kokoro, "second"));
            f.Audit.Events.Should().OnlyContain(item => item.Initiator == SecurityAuditInitiator.LocalUser);
        }
    }

    [Theory]
    [InlineData("persisted")]
    [InlineData("catalogue")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("invalid")]
    [InlineData("unavailable")]
    public async Task InstalledVoiceFinalReadbackFailureHoldsOutputUnavailableWithoutClaimingRollback(string loss)
    {
        using var f = new Fixture();
        var message = SpeechSuggestion("set speech.voice to kokoro / second");
        InstalledSpeechVoiceSuggestionReview review;
        using (BeginSpeechInput(SpeechInput(message)))
        {
            review = await InstalledSpeechVoiceSuggestionReview.ReviewAsync(f.Service, f.Policy, new SuggestionClock(),
                message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        using (review)
        using (BeginSpeechInput(SpeechInput(message)))
        {
            f.Audit.AfterSucceeded = () =>
            {
                if (loss is "persisted") { f.Preferences.Selection = SpeechSelection.Default; }
                if (loss is "catalogue") { f.Catalog.Voices = []; }
                if (loss is "io") { f.Preferences.LoadFailure = new IOException("readback unavailable"); }
                if (loss is "access") { f.Preferences.LoadFailure = new UnauthorizedAccessException("readback denied"); }
                if (loss is "invalid") { f.Preferences.LoadFailure = new InvalidDataException("readback invalid"); }
                if (loss is "unavailable") { f.Catalog.Failure = new InvalidOperationException("discovery unavailable"); }
            };
            await review.Invoking(item => item.ConfirmAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidDataException>().WithMessage("*unconfirmed*No automatic retry or rollback*");
            f.Service.Get().IsAvailable.Should().BeFalse();
            f.Service.Get().Recovery.Should().Contain("bytes may have persisted");
            f.Preferences.Writes.Should().Be(1);
            f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            review.Available.Should().BeFalse();
        }
    }
}
