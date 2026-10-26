using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Core.Voice;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, "set speech.summary-words to 40", 3, 40)]
    [InlineData(true, "set speech.summary-words to 40", 3, 40)]
    [InlineData(false, "set speech.summary-sentences to 2", 2, 80)]
    [InlineData(true, "set speech.summary-sentences to 2", 2, 80)]
    public async Task SpeechSuggestionNativeAndCompactRoutesUseExactCanonicalAnswerAndOneAuditedWrite(
        bool compact, string answerText, int sentences, int words)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var preferences = new LocalTextToSpeechPreferences(f.Paths, NullLogger<LocalTextToSpeechPreferences>.Instance);
            var audit = new AuditLog();
            var speech = new SpeechConfigurationService(preferences, new SummaryCatalog(), audit,
                NullLogger<SpeechConfigurationService>.Instance);
            using var policy = new CallCommunicationPolicy(new SummaryCallState());
            var adapter = new SummaryAnswerAdapter(answerText);
            await using var composed = new Composition(f, adapter: adapter, speech: speech, speechPolicy: policy);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest a word limit of forty for future ordinary spoken responses.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            answer.HasAssistantResponse.Should().BeTrue();
            preferences.LoadSummaryLimits().Should().BeNull();
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
                model.RegisteredSettingSuggestionDescription.Should().Be(model.SpeechSummarySuggestionDescription);
                model.SpeechSummarySuggestionDescription.Should().Contain("Current: 3 sentences, 80 words (default)")
                    .And.Contain($"Proposed: {sentences} sentences, {words} words").And.Contain("shared device-local")
                    .And.Contain("NEXT ordinary speech");
                preferences.LoadSummaryLimits().Should().BeNull();
                audit.Events.Should().BeEmpty();
                window.FindControl<Button>("ConfirmRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.ConversationStatus.Should().NotContain("Unconfirmed");
                new LocalTextToSpeechPreferences(f.Paths, NullLogger<LocalTextToSpeechPreferences>.Instance)
                    .LoadSummaryLimits().Should().Be(new SpokenSummaryLimits(sentences, words));
                audit.Events.Select(item => item.Outcome).Should()
                    .Equal(Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
                model.SpeechSummarySuggestionDescription.Should().BeEmpty();
                model.CanConfirmSpeechSummarySuggestion.Should().BeFalse();
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
    [InlineData("close")]
    [InlineData("settings")]
    public async Task SpeechSuggestionNativeLifetimeRejectsCopiesAndRetiresSourceWithoutLateWrites(string transition)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var preferences = new LocalTextToSpeechPreferences(f.Paths, NullLogger<LocalTextToSpeechPreferences>.Instance);
        var speech = new SpeechConfigurationService(preferences, new SummaryCatalog(), new AuditLog(),
            NullLogger<SpeechConfigurationService>.Instance);
        using var policy = new CallCommunicationPolicy(new SummaryCallState());
        await using var composed = new Composition(f, adapter: new SummaryAnswerAdapter("set speech.summary-words to 40"),
            speech: speech, speechPolicy: policy);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Suggest a future ordinary speech cap.";
        await model.SubmitConversationAsync();
        var answer = model.ConversationMessages.Single();
        await model.ReviewSpeechSummarySuggestionAsync(answer with { });
        model.CanConfirmSpeechSummarySuggestion.Should().BeFalse();
        await model.ReviewSpeechSummarySuggestionAsync(answer);
        model.CanConfirmSpeechSummarySuggestion.Should().BeTrue();
        if (transition is "cancel") { model.CancelSpeechSummarySuggestion(); }
        if (transition is "refresh") { await model.ReadConversationAsync(); }
        if (transition is "source")
        {
            var request = new HostRequest(new(Guid.NewGuid()), answer.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
            using var input = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
            var preview = await f.Store.PreviewDispositionAsync(answer.Request.SessionId, answer.Generation, 0, f.Token);
            await f.Tasks.CommitAsync(new(request, new(1), HostTaskState.IntentRecorded), 0, f.Token);
            await f.Store.DisposeSessionAsync(request, preview, () => true, f.Token);
        }
        if (transition is "private") { composed.Access.CanInspect = false; }
        if (transition is "close") { model.Close(); }
        if (transition is "settings") { preferences.SaveSummaryLimits(new(2, 60)); speech.Reload(); }
        await model.ConfirmSpeechSummarySuggestionAsync();
        preferences.LoadSummaryLimits().Should().Be(transition is "settings" ? new SpokenSummaryLimits(2, 60) : null);
    }

    private sealed class SummaryAnswerAdapter(string text) : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            Encoding.UTF8.GetString(context.Span).Should().Contain("speech.summary-words").And.Contain("one inert suggestion");
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection, new(text, null)));
        }
    }

    private sealed class SummaryCatalog : ISpeechCatalog
    {
        public IReadOnlyList<SpeechProvider> GetProviders() => [];
        public IReadOnlyList<SpeechVoice> GetVoices() => [];
        public SpeechVoice? GetDefaultVoice() => null;
    }

    private sealed class SummaryCallState : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
}
