using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
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
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task InstalledVoiceFullAndCompactControlsReviewExactRetainedAnswerAndAtomicallySaveOrCancel(bool compact, bool cancel, bool maximumId)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var preferences = new LocalTextToSpeechPreferences(f.Paths, NullLogger<LocalTextToSpeechPreferences>.Instance);
            var catalogue = new InstalledSuggestionCatalogue();
            var voiceId = maximumId ? new string('v', SpeechSelection.MaximumVoiceIdLength) : "shared";
            catalogue.Voices = catalogue.Voices.Select(item => string.Equals(item.ProviderId, SpeechProviderIds.Kokoro, StringComparison.Ordinal)
                ? item with { Id = voiceId } : item).ToArray();
            var audit = new AuditLog();
            var speech = new SpeechConfigurationService(preferences, catalogue, audit, NullLogger<SpeechConfigurationService>.Instance);
            using var policy = new CallCommunicationPolicy(new SummaryCallState());
            var adapter = new SummaryAnswerAdapter("set speech.voice to kokoro / " + voiceId);
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            model.BindInstalledSpeechVoiceSuggestions((message, live, validate, token) =>
                InstalledSpeechVoiceSuggestionReview.ReviewAsync(speech, policy, f.Time, message, live, validate, token),
                (review, token) => review.ConfirmAsync(token));
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = $"Suggest exactly kokoro / {voiceId} for future spoken answers.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            preferences.LoadSelection().Should().BeNull();
            audit.Events.Should().BeEmpty();
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
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue(model.ConversationStatus);
                model.RegisteredSettingSuggestionDescription.Should().Contain("Current provider: windows-sapi; voice: provider default (default)")
                    .And.Contain("Proposed provider: kokoro; voice: " + voiceId).And.Contain("shared device-local")
                    .And.Contain("exact successful retained answer").And.Contain("NEXT separately requested");
                preferences.LoadSelection().Should().BeNull();
                audit.Events.Should().BeEmpty();
                window.FindControl<Button>(cancel ? "CancelRegisteredSettingSuggestion" : "ConfirmRegisteredSettingSuggestion")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                var expected = cancel ? null : new SpeechSelection(SpeechProviderIds.Kokoro, voiceId);
                new LocalTextToSpeechPreferences(f.Paths, NullLogger<LocalTextToSpeechPreferences>.Instance)
                    .LoadSelection().Should().Be(expected);
                new SpeechConfigurationService(preferences, catalogue, audit, NullLogger<SpeechConfigurationService>.Instance)
                    .Get().Selection.Should().Be(expected ?? SpeechSelection.Default);
                if (cancel)
                {
                    model.ConversationStatus.Should().Contain("cancelled");
                    audit.Events.Should().BeEmpty();
                }
                else
                {
                    model.ConversationStatus.Should().Contain("exact persisted/current readback");
                    speech.Get().EffectiveVoice!.ConfigurationId.Should().Be("kokoro / " + voiceId);
                    audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                    audit.Events.Should().OnlyContain(item => item.TargetId == "preferences.device-local");
                }
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                adapter.Calls.Should().Be(1);
                (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token)).Should().Be(answer);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("close")]
    [InlineData("settings")]
    [InlineData("catalogue")]
    [InlineData("write")]
    [InlineData("unbind")]
    public async Task InstalledVoiceNativeReviewRejectsCopiedSourceAndRetiredPageOrCatalogueWithoutMutation(string loss)
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
        var model = composed.Model;
        model.BindInstalledSpeechVoiceSuggestions((message, live, validate, token) =>
            InstalledSpeechVoiceSuggestionReview.ReviewAsync(speech, policy, f.Time, message, live, validate, token),
            (review, token) => review.ConfirmAsync(token));
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Suggest exactly kokoro / shared for future spoken answers.";
        await model.SubmitConversationAsync();
        var answer = model.ConversationMessages.Single();
        await model.ReviewRegisteredSettingSuggestionAsync(answer with { });
        model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
        await model.ReviewRegisteredSettingSuggestionAsync(answer);
        model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue();
        if (string.Equals(loss, "refresh", StringComparison.Ordinal)) { await model.ReadConversationAsync(); }
        if (string.Equals(loss, "source", StringComparison.Ordinal))
        {
            var request = new HostRequest(new(Guid.NewGuid()), answer.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            using var input = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
            var preview = await f.Store.PreviewDispositionAsync(answer.Request.SessionId, answer.Generation, 0, f.Token);
            await f.Tasks.CommitAsync(new(request, new(1), HostTaskState.IntentRecorded), 0, f.Token);
            await f.Store.DisposeSessionAsync(request, preview, () => true, f.Token);
        }
        if (string.Equals(loss, "private", StringComparison.Ordinal)) { composed.Access.CanInspect = false; }
        if (string.Equals(loss, "close", StringComparison.Ordinal)) { model.Close(); }
        if (string.Equals(loss, "settings", StringComparison.Ordinal)) { preferences.SaveSelection(new(SpeechProviderIds.Windows, "shared")); }
        if (string.Equals(loss, "catalogue", StringComparison.Ordinal)) { catalogue.Voices = catalogue.Voices.Take(1).ToArray(); }
        if (string.Equals(loss, "write", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.Combine(f.Paths.LocalRoot, "Preferences", "speech-selection.txt"));
        }
        if (string.Equals(loss, "unbind", StringComparison.Ordinal))
        {
            model.BindInstalledSpeechVoiceSuggestions((_, _, _, _) => throw new InvalidOperationException("unavailable"),
                (_, _) => throw new InvalidOperationException("unavailable"));
        }
        await model.ConfirmRegisteredSettingSuggestionAsync();
        if (!string.Equals(loss, "write", StringComparison.Ordinal))
        { preferences.LoadSelection().Should().Be(string.Equals(loss, "settings", StringComparison.Ordinal) ? new SpeechSelection(SpeechProviderIds.Windows, "shared") : null); }
        model.ConversationStatus.Should().NotContain("exact persisted/current readback");
        audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        adapter.Calls.Should().Be(1);
    }

    private sealed class InstalledSuggestionCatalogue : ISpeechCatalog
    {
        public IReadOnlyList<SpeechVoice> Voices { get; set; } =
        [
            new("shared", "Windows", "en-US", SpeechVoiceGender.Female),
            new SpeechVoice("shared", "Kokoro", "en-US", SpeechVoiceGender.Female) { ProviderId = SpeechProviderIds.Kokoro },
        ];
        public IReadOnlyList<SpeechProvider> GetProviders() =>
        [
            new(SpeechProviderIds.Windows, "Windows", "Built-in", true, true, null, "shared"),
            new(SpeechProviderIds.Kokoro, "Kokoro", "Installed", true, false, 1, null),
        ];
        public IReadOnlyList<SpeechVoice> GetVoices() => Voices;
        public SpeechVoice? GetDefaultVoice() => throw new InvalidOperationException("Review must never ask a synthesis adapter for fallback.");
    }
}
