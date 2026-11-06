using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, "Off")]
    [InlineData(true, "Off")]
    [InlineData(false, "weekly Mon,Fri 22:00 06:00 Australia/Sydney")]
    [InlineData(true, "weekly Mon,Fri 22:00 06:00 Australia/Sydney")]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public async Task QuietHoursNativeRetainedAnswerCompletesActualAtomicStoreAndAuditReadback(bool compact, string? value)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new HostTaskCoordinator(f.Tasks));
            var preferences = new LocalQuietHoursPreferences(f.Paths);
            var previous = QuietHoursSchedule.Weekly("Mon,Tue,Wed,Thu,Fri", "09:00", "17:00", "UTC");
            preferences.Save(previous);
            var audit = new AuditLog();
            var quiet = new QuietHoursConfigurationService(preferences, admission, audit, f.Time,
                NullLogger<QuietHoursConfigurationService>.Instance);
            var timingPreferences = new LocalVoiceCaptureTimingPreferences(f.Paths);
            var originalTiming = new VoiceCaptureTiming(7, 20, 2);
            timingPreferences.Save(originalTiming);
            var timing = new VoiceCaptureTimingConfigurationService(timingPreferences, admission, audit,
                NullLogger<VoiceCaptureTimingConfigurationService>.Instance);
            using var policy = new CallCommunicationPolicy(new SummaryCallState());
            var adapter = new SummaryAnswerAdapter(value is null ? "reset notices.quiet-hours" : "set notices.quiet-hours to " + value);
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            model.BindQuietHoursSuggestions((message, eligible, validate, token) =>
                QuietHoursSuggestionReview.ReviewAsync(quiet, policy, message, eligible, validate, token),
                (review, token) => review.ConfirmAsync(token));
            model.BindVoiceCaptureTimingSuggestions((message, eligible, validate, token) =>
                VoiceCaptureTimingSuggestionReview.ReviewAsync(timing, policy, f.Time, message, eligible, validate, token),
                (review, token) => review.ConfirmAsync(token));
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest quiet routine completion speech during this explicit weekly period.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            await ActivateOriginalRequestWindow(window);
            try
            {
                var button = window.GetVisualDescendants().OfType<Button>()
                    .Single(item => string.Equals(item.Name, "ReviewRegisteredSettingSuggestion", StringComparison.Ordinal));
                button.DataContext.Should().BeSameAs(answer);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.RegisteredSettingSuggestionDescription.Should().Contain("Current saved: " + previous)
                    .And.Contain("Proposed: " + (value ?? "reset to default Off (not saved)"))
                    .And.Contain("Default: Off").And.Contain("shared device-local").And.Contain("future already-consented")
                    .And.Contain("listening").And.Contain("ordinary requested answers");
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue(model.ConversationStatus);
                preferences.Load().Should().Be(previous);
                audit.Events.Should().BeEmpty();
                window.FindControl<Button>("ConfirmRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.ConversationStatus.Should().Contain("saved with committed preference and audit readback");
                var expected = value is null ? null : QuietHoursSchedule.Parse(value);
                new LocalQuietHoursPreferences(f.Paths).Load().Should().Be(expected);
                quiet.Get().Effective.Should().Be(expected ?? QuietHoursSchedule.Off);
                timingPreferences.Load().Should().Be(originalTiming);
                timing.GetTiming().Should().Be(originalTiming);
                audit.Events.Select(item => (item.ActionId, item.Initiator, item.Outcome)).Should().Equal(
                    ("configuration.notices-quiet-hours", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Requested),
                    ("configuration.notices-quiet-hours", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Succeeded));
                model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                adapter.Calls.Should().Be(1);
                (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token)).Should().Be(answer);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("refresh")]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("close")]
    [InlineData("settings")]
    [InlineData("hide-reopen")]
    [InlineData("write-unconfirmed")]
    [InlineData("cancel-await")]
    [InlineData("cancel-confirm-await")]
    [InlineData("voice-laundering")]
    public async Task QuietHoursNativeReviewRejectsForeignCopiesAndRevokedOrAwaitedSources(string transition)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new HostTaskCoordinator(f.Tasks));
            var preferences = new LocalQuietHoursPreferences(f.Paths);
            var audit = new AuditLog();
            var quiet = new QuietHoursConfigurationService(preferences, admission, audit, f.Time,
                NullLogger<QuietHoursConfigurationService>.Instance);
            using var policy = new CallCommunicationPolicy(new SummaryCallState());
            await using var composed = new Composition(f, adapter: new SummaryAnswerAdapter("set notices.quiet-hours to Off"));
            var model = composed.Model;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sourceReads = 0;
            model.BindQuietHoursSuggestions(async (message, eligible, validate, token) =>
            {
                async Task Validate(CancellationToken cancellation)
                {
                    sourceReads++;
                    if (transition is "cancel-await" || transition is "cancel-confirm-await" && sourceReads == 3)
                    { entered.TrySetResult(); await release.Task.WaitAsync(cancellation); }
                    await validate(cancellation);
                }
                return await QuietHoursSuggestionReview.ReviewAsync(quiet, policy, message, eligible, Validate, token);
            }, (review, token) => review.ConfirmAsync(token));
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest quiet hours Off.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            var owner = new SessionsPresentationOwner(model);
            var window = new SessionsWindow(model, owner);
            window.Show();
            await ActivateOriginalRequestWindow(window);
            try
            {
                await model.ReviewRegisteredSettingSuggestionAsync(answer with { });
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                if (transition is "voice-laundering")
                {
                    using var voice = HostActivity.BeginRoot(new(new(Guid.NewGuid()), answer.Request.SessionId,
                        new(Guid.NewGuid()), RequestOrigin.ActivatedVoice), HostActivityLayer.Desktop, HostOperation.Request);
                    await model.ReviewRegisteredSettingSuggestionAsync(answer);
                }
                else
                {
                    var work = model.ReviewRegisteredSettingSuggestionAsync(answer);
                    if (transition is "cancel-confirm-await")
                    {
                        await work;
                        work = model.ConfirmRegisteredSettingSuggestionAsync();
                    }
                    if (transition is "cancel-await" or "cancel-confirm-await")
                    {
                        await Task.WhenAny(entered.Task, work);
                        entered.Task.IsCompleted.Should().BeTrue(model.ConversationStatus);
                        model.CanCancelRegisteredSettingSuggestion.Should().BeTrue();
                        var cancel = window.FindControl<Button>("CancelRegisteredSettingSuggestion")!;
                        cancel.IsEffectivelyEnabled.Should().BeTrue();
                        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        model.CanCancelRegisteredSettingSuggestion.Should().BeFalse();
                        release.TrySetResult();
                    }
                    await work;
                }
                if (transition is not ("cancel-await" or "cancel-confirm-await" or "voice-laundering"))
                { model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue(model.ConversationStatus); }
                if (transition is "cancel") { model.CancelRegisteredSettingSuggestion(); }
                if (transition is "refresh") { await model.ReadConversationAsync(); }
                if (transition is "private") { composed.Access.CanInspect = false; }
                if (transition is "control") { composed.Access.ControlRevision++; }
                if (transition is "close") { model.Close(); }
                if (transition is "settings") { preferences.Save(QuietHoursSchedule.Off); }
                if (transition is "hide-reopen") { window.Hide(); window.Show(); }
                if (transition is "write-unconfirmed") { preferences.BeginWrite(); }
                if (transition is "source")
                {
                    var request = new HostRequest(new(Guid.NewGuid()), answer.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
                    using var input = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
                    var preview = await f.Store.PreviewDispositionAsync(answer.Request.SessionId, answer.Generation, 0, f.Token);
                    await f.Tasks.CommitAsync(new(request, new(1), HostTaskState.IntentRecorded), 0, f.Token);
                    await f.Store.DisposeSessionAsync(request, preview, () => true, f.Token);
                }
                await model.ConfirmRegisteredSettingSuggestionAsync();
                preferences.ReadBack().Should().Be(transition is "settings" ? QuietHoursSchedule.Off : null);
                audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                if (transition is "cancel-await" or "cancel-confirm-await")
                {
                    audit.Events.Should().BeEmpty();
                    model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                    model.CanCancelRegisteredSettingSuggestion.Should().BeFalse();
                }
            }
            finally { owner.Revoke(); }
        });
    }
}
