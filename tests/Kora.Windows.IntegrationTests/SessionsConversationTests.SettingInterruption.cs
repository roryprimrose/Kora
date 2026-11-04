using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false, "cancel")]
    [InlineData(false, true, "cancel")]
    [InlineData(true, false, "cancel")]
    [InlineData(true, true, "cancel")]
    [InlineData(false, false, "family-cancel")]
    [InlineData(false, true, "family-cancel")]
    [InlineData(false, true, "settings")]
    [InlineData(true, true, "settings")]
    public async Task RegisteredSettingInterruptionRetiresAwaitedReviewAndConfirmationBeforeFreshRecovery(
        bool compact, bool confirming, string interruption)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var preferences = new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths),
                NullLogger<LocalAppearancePreferences>.Instance);
            var audit = new AuditLog();
            var appearance = new AppearanceConfigurationService(preferences, audit,
                NullLogger<AppearanceConfigurationService>.Instance);
            var adapter = new AppearanceAnswerAdapter("set appearance.theme to dark");
            await using var composed = new Composition(f, adapter: adapter);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sourceReads = 0;
            var held = false;
            AppearanceSuggestionReview? interrupted = null;
            var sink = new WindowsSqliteEvidenceSink(f.Paths);
            sink.Initialize();
            var model = new SessionsViewModel(composed.Workspace,
                new(new WindowsSqliteEvidenceReader(sink), composed.Access, f.Time, NullLogger<DurableEvidenceQuery>.Instance),
                composed.Access, NullLogger<SessionsViewModel>.Instance, conversation: composed.Conversation,
                reviewAppearanceSuggestion: async (message, eligible, validate, token) =>
                {
                    var review = await AppearanceSuggestionReview.ReviewAsync(appearance, message, eligible,
                        async cancellation =>
                        {
                            await validate(cancellation);
                            if (confirming && ++sourceReads == 2)
                            {
                                held = true;
                                entered.SetResult();
                                await release.Task;
                            }
                        }, token);
                    if (!confirming && !held)
                    {
                        held = true;
                        entered.SetResult();
                        await release.Task;
                    }
                    interrupted ??= review;
                    return review;
                },
                confirmAppearanceSuggestion: (review, token) => review.ConfirmAsync(token));
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest a dark theme.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            model.ComposerBody = "Keep this unsent message.";
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                var operation = model.ReviewRegisteredSettingSuggestionAsync(answer);
                if (confirming)
                {
                    await operation;
                    operation = model.ConfirmRegisteredSettingSuggestionAsync();
                }
                await entered.Task;
                Dispatcher.UIThread.RunJobs();
                var cancel = window.FindControl<Button>("CancelRegisteredSettingSuggestion")!;
                cancel.IsEffectivelyEnabled.Should().BeTrue("native cancellation must remain available while the setting operation is pending");
                switch (interruption)
                {
                    case "settings":
                        preferences.SaveThemeMode(ApplicationThemeMode.Light);
                        appearance.Reload();
                        break;
                    case "family-cancel":
                        model.CancelAppearanceSuggestion();
                        break;
                    default:
                        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        break;
                }
                release.SetResult();
                await operation;
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse("interruption must retire work already awaiting source validation");
                model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                interrupted.Should().NotBeNull();
                interrupted!.Available.Should().BeFalse("late completion cannot restore retired consent");
                preferences.LoadThemeMode().Should().Be(string.Equals(interruption, "settings", StringComparison.Ordinal)
                    ? ApplicationThemeMode.Light : null);
                audit.Events.Should().BeEmpty("interrupted review/confirmation must never reach the audited writer");
                model.ComposerBody.Should().Be("Keep this unsent message.");
                await model.ConfirmRegisteredSettingSuggestionAsync();
                audit.Events.Should().BeEmpty("an interrupted confirmation cannot be replayed");

                await model.ReviewRegisteredSettingSuggestionAsync(answer);
                model.RegisteredSettingSuggestionDescription.Should().Contain(string.Equals(interruption, "settings", StringComparison.Ordinal)
                    ? "current light (saved); proposed dark" : "current system (default); proposed dark");
                await model.ConfirmRegisteredSettingSuggestionAsync();
                preferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
                var reopened = new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths),
                    NullLogger<LocalAppearancePreferences>.Instance);
                reopened.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
                audit.Events.Select(item => item.Outcome).Should()
                    .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                window.FindControl<Button>("CancelRegisteredSettingSuggestion")!.IsEnabled.Should().BeFalse();
                adapter.Calls.Should().Be(1, "recovery rereads the retained answer rather than regenerating it");
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token))
                    .Should().Be(answer);
                model.ComposerBody.Should().Be("Keep this unsent message.");
            }
            finally
            {
                release.TrySetResult();
                owner.Revoke();
            }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InstalledVoiceSettingInterruptionRetiresAwaitedReviewAndConfirmationBeforeFreshRecovery(
        bool compact, bool confirming)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var preferences = new LocalTextToSpeechPreferences(f.Paths, NullLogger<LocalTextToSpeechPreferences>.Instance);
            var catalogue = new InstalledSuggestionCatalogue();
            var audit = new AuditLog();
            var speech = new SpeechConfigurationService(preferences, catalogue, audit, NullLogger<SpeechConfigurationService>.Instance);
            using var policy = new CallCommunicationPolicy(new SummaryCallState());
            var adapter = new SummaryAnswerAdapter("set speech.voice to kokoro / shared");
            await using var composed = new Composition(f, adapter: adapter);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sourceReads = 0;
            var held = false;
            InstalledSpeechVoiceSuggestionReview? interrupted = null;
            var model = composed.Model;
            model.BindInstalledSpeechVoiceSuggestions(async (message, eligible, validate, token) =>
            {
                var review = await InstalledSpeechVoiceSuggestionReview.ReviewAsync(speech, policy, f.Time, message, eligible,
                    async cancellation =>
                    {
                        await validate(cancellation);
                        if (confirming && ++sourceReads == 2)
                        {
                            held = true;
                            entered.SetResult();
                            await release.Task;
                        }
                    }, token);
                if (!confirming && !held)
                {
                    held = true;
                    entered.SetResult();
                    await release.Task;
                }
                interrupted ??= review;
                return review;
            }, (review, token) => review.ConfirmAsync(token));
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest exactly kokoro / shared for future spoken answers.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            model.ComposerBody = "Keep this unsent message.";
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                var operation = model.ReviewRegisteredSettingSuggestionAsync(answer);
                if (confirming)
                {
                    await operation;
                    operation = model.ConfirmRegisteredSettingSuggestionAsync();
                }
                await entered.Task;
                Dispatcher.UIThread.RunJobs();
                var cancel = window.FindControl<Button>("CancelRegisteredSettingSuggestion")!;
                cancel.IsEffectivelyEnabled.Should().BeTrue();
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.CanCancelRegisteredSettingSuggestion.Should().BeFalse();
                release.SetResult();
                await operation;
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                interrupted.Should().NotBeNull();
                interrupted!.Available.Should().BeFalse();
                preferences.LoadSelection().Should().BeNull();
                audit.Events.Should().BeEmpty("cancelled awaited work must not reach the native writer");
                await model.ConfirmRegisteredSettingSuggestionAsync();
                audit.Events.Should().BeEmpty("retired confirmation cannot be replayed");

                await model.ReviewRegisteredSettingSuggestionAsync(answer);
                model.RegisteredSettingSuggestionDescription.Should().Contain("Proposed provider: kokoro; voice: shared");
                await model.ConfirmRegisteredSettingSuggestionAsync();
                var expected = new SpeechSelection("kokoro", "shared");
                new LocalTextToSpeechPreferences(f.Paths, NullLogger<LocalTextToSpeechPreferences>.Instance)
                    .LoadSelection().Should().Be(expected);
                speech.Get().Selection.Should().Be(expected);
                model.ConversationStatus.Should().Contain("exact persisted/current readback");
                audit.Events.Select(item => item.Outcome).Should()
                    .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                model.CanCancelRegisteredSettingSuggestion.Should().BeFalse();
                adapter.Calls.Should().Be(1);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token))
                    .Should().Be(answer);
                model.ComposerBody.Should().Be("Keep this unsent message.");
            }
            finally
            {
                release.TrySetResult();
                owner.Revoke();
            }
        });
    }
}
