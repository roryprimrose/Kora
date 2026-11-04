using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CaptureTimingExpandedAndCompactReviewExactRetainedAnswerAndConfirmAuditedFutureOnlySave(bool compact, bool reset)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var storage = new InteractionStorageFixture();
            await storage.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(storage);
            await using var admission = new AudioControlAdmission(storage.Store, storage.Store, new HostTaskCoordinator(storage.Tasks));
            var preferences = new LocalVoiceCaptureTimingPreferences(storage.Paths);
            preferences.Save(new(7, 20, 2));
            var audit = new AuditLog();
            var timing = new VoiceCaptureTimingConfigurationService(preferences, admission, audit,
                NullLogger<VoiceCaptureTimingConfigurationService>.Instance);
            using var policy = new CallCommunicationPolicy(new SummaryCallState());
            var adapter = new SummaryAnswerAdapter(reset ? "reset voice.trailing-silence" : "set voice.trailing-silence to 2.5000001");
            await using var composed = new Composition(storage, adapter: adapter);
            var model = composed.Model;
            model.BindVoiceCaptureTimingSuggestions((message, eligible, validate, token) =>
                VoiceCaptureTimingSuggestionReview.ReviewAsync(timing, policy, storage.Time, message, eligible, validate, token),
                (review, token) => review.ConfirmAsync(token));
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Allow a longer pause before capture stops.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                var button = window.GetVisualDescendants().OfType<Button>()
                    .Single(item => string.Equals(item.Name, "ReviewRegisteredSettingSuggestion", StringComparison.Ordinal));
                button.DataContext.Should().BeSameAs(answer);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                var expected = new VoiceCaptureTiming(7, 20, reset ? 1 : 2.5000001m);
                model.RegisteredSettingSuggestionDescription.Should().Contain("command start 7 s; reply start 20 s; trailing silence 2 s")
                    .And.Contain(expected.Describe()).And.Contain("future eligible captures only").And.Contain("saved tuple");
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue();
                preferences.Load().Should().Be(new VoiceCaptureTiming(7, 20, 2));
                audit.Events.Should().BeEmpty();
                window.FindControl<Button>("ConfirmRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.ConversationStatus.Should().Contain("saved with committed preference and audit readback");
                new LocalVoiceCaptureTimingPreferences(storage.Paths).Load().Should().Be(expected);
                timing.GetTiming().Should().Be(expected);
                audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                adapter.Calls.Should().Be(1);
                (await storage.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, storage.Token)).Should().Be(answer);
                storage.Count("host_questions").Should().Be(0);
                storage.Count("scoped_grants").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("refresh")]
    [InlineData("private")]
    [InlineData("close")]
    [InlineData("settings")]
    [InlineData("expiry")]
    [InlineData("write")]
    [InlineData("rebind")]
    [InlineData("source")]
    public async Task CaptureTimingNativeSourceCopiesAndRetiredReviewsCannotApply(string transition)
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        await using var admission = new AudioControlAdmission(storage.Store, storage.Store, new HostTaskCoordinator(storage.Tasks));
        var preferences = new LocalVoiceCaptureTimingPreferences(storage.Paths);
        var audit = new AuditLog();
        var timing = new VoiceCaptureTimingConfigurationService(preferences, admission, audit,
            NullLogger<VoiceCaptureTimingConfigurationService>.Instance);
        using var policy = new CallCommunicationPolicy(new SummaryCallState());
        await using var composed = new Composition(storage, adapter: new SummaryAnswerAdapter("set voice.trailing-silence to 2"));
        var model = composed.Model;
        model.BindVoiceCaptureTimingSuggestions((message, eligible, validate, token) =>
            VoiceCaptureTimingSuggestionReview.ReviewAsync(timing, policy, storage.Time, message, eligible, validate, token),
            (review, token) => review.ConfirmAsync(token));
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Allow a longer pause.";
        await model.SubmitConversationAsync();
        var answer = model.ConversationMessages.Single();
        await model.ReviewRegisteredSettingSuggestionAsync(answer with { });
        model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
        await model.ReviewRegisteredSettingSuggestionAsync(answer);
        model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue();
        switch (transition)
        {
            case "cancel": model.CancelRegisteredSettingSuggestion(); break;
            case "refresh": await model.ReadConversationAsync(); break;
            case "private": composed.Access.CanInspect = false; break;
            case "close": model.Close(); break;
            case "settings": preferences.Save(new(7, 20, 2)); timing.Reload(); break;
            case "expiry": storage.Time.Now = storage.Time.Now.AddSeconds(60); break;
            case "write": preferences.BeginWrite(); break;
            case "rebind":
                model.BindVoiceCaptureTimingSuggestions((message, eligible, validate, token) =>
                    VoiceCaptureTimingSuggestionReview.ReviewAsync(timing, policy, storage.Time, message, eligible, validate, token),
                    (review, token) => review.ConfirmAsync(token));
                break;
            case "source":
                var request = new HostRequest(new(Guid.NewGuid()), answer.Request.SessionId,
                    new(Guid.NewGuid()), RequestOrigin.LocalUi);
                using (var input = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request))
                {
                    var preview = await storage.Store.PreviewDispositionAsync(answer.Request.SessionId, answer.Generation, 0, storage.Token);
                    await storage.Tasks.CommitAsync(new(request, new(1), HostTaskState.IntentRecorded), 0, storage.Token);
                    await storage.Store.DisposeSessionAsync(request, preview, static () => true, storage.Token);
                }
                break;
        }
        await model.ConfirmRegisteredSettingSuggestionAsync();
        preferences.ReadBack().Should().Be(transition is "settings" ? new VoiceCaptureTiming(7, 20, 2) : null);
        audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureTimingCancellationDuringAwaitRetiresPendingReviewOrConfirmation(bool confirming)
    {
        using var storage = new InteractionStorageFixture();
        await storage.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(storage);
        await using var admission = new AudioControlAdmission(storage.Store, storage.Store, new HostTaskCoordinator(storage.Tasks));
        var preferences = new LocalVoiceCaptureTimingPreferences(storage.Paths);
        var audit = new AuditLog();
        var timing = new VoiceCaptureTimingConfigurationService(preferences, admission, audit,
            NullLogger<VoiceCaptureTimingConfigurationService>.Instance);
        using var policy = new CallCommunicationPolicy(new SummaryCallState());
        await using var composed = new Composition(storage, adapter: new SummaryAnswerAdapter("set voice.trailing-silence to 2"));
        var model = composed.Model;
        var waiting = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.BindVoiceCaptureTimingSuggestions((message, eligible, validate, token) =>
            VoiceCaptureTimingSuggestionReview.ReviewAsync(timing, policy, storage.Time, message, eligible, async ct =>
            {
                await validate(ct);
                if (waiting) { entered.TrySetResult(); await release.Task; }
            }, token), (review, token) => review.ConfirmAsync(token));
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Allow a longer pause.";
        await model.SubmitConversationAsync();
        var answer = model.ConversationMessages.Single();
        if (confirming) { await model.ReviewRegisteredSettingSuggestionAsync(answer); }
        waiting = true;
        var work = confirming ? model.ConfirmRegisteredSettingSuggestionAsync() : model.ReviewRegisteredSettingSuggestionAsync(answer);
        await entered.Task;
        model.CanCancelRegisteredSettingSuggestion.Should().BeTrue();
        model.CancelRegisteredSettingSuggestion();
        work.IsCompleted.Should().BeFalse();
        release.SetResult();
        await work;
        model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
        preferences.ReadBack().Should().BeNull();
        audit.Events.Should().BeEmpty();
        waiting = false;
        await model.ReviewRegisteredSettingSuggestionAsync(answer);
        model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue();
        await model.ConfirmRegisteredSettingSuggestionAsync();
        preferences.Load().Should().Be(new VoiceCaptureTiming(5, 15, 2));
    }
}
