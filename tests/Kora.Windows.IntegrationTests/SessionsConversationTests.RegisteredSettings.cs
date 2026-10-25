using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Fact]
    public async Task RegisteredSettingReviewReportsPersistedButUnconfirmedWriteWithoutRetryOrRollback()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var preferences = new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths),
            NullLogger<LocalAppearancePreferences>.Instance);
        var audit = new UnconfirmedSettingAudit();
        var appearance = new AppearanceConfigurationService(preferences, audit,
            NullLogger<AppearanceConfigurationService>.Instance);
        await using var composed = new Composition(f, adapter: new AppearanceAnswerAdapter(false), appearance: appearance);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Suggest a dark theme.";
        await model.SubmitConversationAsync();
        await model.ReviewRegisteredSettingSuggestionAsync(model.ConversationMessages.Single());
        await model.ConfirmRegisteredSettingSuggestionAsync();
        preferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark, "the failed terminal receipt cannot promise rollback");
        model.ConversationStatus.Should().Contain("save is unconfirmed").And.Contain("a write may have persisted")
            .And.Contain("No automatic retry or rollback");
        model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
        model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
        audit.Events.Should().ContainSingle().Which.Outcome.Should().Be(SecurityAuditOutcome.Requested);
        await model.ConfirmRegisteredSettingSuggestionAsync();
        audit.Events.Should().ContainSingle("a consumed review cannot retry an uncertain save");
    }

    [Fact]
    public async Task RegisteredSettingReviewReplacesTheExactFamilyWithoutKeepingOldConfirmationAuthority()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var audit = new AuditLog();
        var appearance = new AppearanceConfigurationService(
            new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths), NullLogger<LocalAppearancePreferences>.Instance),
            audit, NullLogger<AppearanceConfigurationService>.Instance);
        var preferences = new LocalTextToSpeechPreferences(f.Paths, NullLogger<LocalTextToSpeechPreferences>.Instance);
        var speech = new SpeechConfigurationService(preferences, new SummaryCatalog(), audit,
            NullLogger<SpeechConfigurationService>.Instance);
        using var policy = new CallCommunicationPolicy(new SummaryCallState());
        var adapter = new SettingAnswers();
        await using var composed = new Composition(f, adapter: adapter, appearance: appearance, speech: speech, speechPolicy: policy);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Suggest a theme.";
        await model.SubmitConversationAsync();
        model.ComposerBody = "Suggest a future speech cap.";
        await model.SubmitConversationAsync();
        await model.ReadConversationAsync();
        var first = model.ConversationMessages.Single(item => string.Equals(item.AssistantResponse?.Body,
            "set appearance.theme to dark", StringComparison.Ordinal));
        var second = model.ConversationMessages.Single(item => string.Equals(item.AssistantResponse?.Body,
            "set speech.summary-words to 40", StringComparison.Ordinal));
        await model.ReviewRegisteredSettingSuggestionAsync(first);
        model.CanConfirmAppearanceSuggestion.Should().BeTrue();
        await model.ReviewRegisteredSettingSuggestionAsync(second);
        model.CanConfirmAppearanceSuggestion.Should().BeFalse();
        model.AppearanceSuggestionDescription.Should().BeEmpty();
        model.CanConfirmSpeechSummarySuggestion.Should().BeTrue();
        model.RegisteredSettingSuggestionDescription.Should().Contain("Proposed: 3 sentences, 40 words");
        audit.Events.Should().BeEmpty();
        await model.ConfirmRegisteredSettingSuggestionAsync();
        appearance.Get(AppearanceOption.Theme).Value.Should().Be(new AppearanceValue.Theme(ApplicationThemeMode.System));
        preferences.LoadSummaryLimits().Should().Be(new SpokenSummaryLimits(3, 40));
        model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
        model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
        audit.Events.Should().HaveCount(2);
        await model.ConfirmRegisteredSettingSuggestionAsync();
        audit.Events.Should().HaveCount(2, "confirmation cannot be replayed");
        adapter.Calls.Should().Be(2, "review never regenerates an answer");
    }

    private sealed class SettingAnswers : IModelTurnAdapter
    {
        internal int Calls { get; private set; }

        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(Calls == 1 ? "set appearance.theme to dark" : "set speech.summary-words to 40", null)));
        }

    }

    private sealed class UnconfirmedSettingAudit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];

        public void Write(SecurityAuditEvent auditEvent)
        {
            if (auditEvent.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("Terminal setting audit unavailable."); }
            Events.Add(auditEvent);
        }
    }
}
