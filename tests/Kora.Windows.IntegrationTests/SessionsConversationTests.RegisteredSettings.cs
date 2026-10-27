using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisteredSettingAndModelMemoryNativeRoutesKeepSeparateConfirmationAndAdmission(bool compact)
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
            var adapter = new SettingAndMemoryAnswers();
            await using var composed = new Composition(f, adapter: adapter, appearance: appearance, memoryContext: true);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest a dark theme.";
            await model.SubmitConversationAsync();
            model.ComposerBody = "Explicitly propose one session-memory preference.";
            await model.SubmitConversationAsync();
            await model.ReadConversationAsync();
            var setting = model.ConversationMessages.Single(item => string.Equals(item.AssistantResponse?.Body,
                "set appearance.theme to dark", StringComparison.Ordinal));
            var memory = model.ConversationMessages.Single(item => string.Equals(item.AssistantResponse?.Body,
                MemoryProposal, StringComparison.Ordinal));
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                async Task Click(string name, ConversationMessage? source = null)
                {
                    if (source is not null)
                    {
                        window.FindControl<ListBox>("AnswerSourceRecords")!.ScrollIntoView(source);
                        window.UpdateLayout();
                        Dispatcher.UIThread.RunJobs();
                    }
                    window.GetVisualDescendants().OfType<Button>()
                        .Single(item => string.Equals(item.Name, name, StringComparison.Ordinal)
                            && (source is null || ReferenceEquals(item.DataContext, source)))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.WhenAll(owner.RegisteredSettingOperation, owner.ModelMemoryOperation);
                }
                await Click("ReviewRegisteredSettingSuggestion", setting);
                model.RegisteredSettingSuggestionDescription.Should().Contain("proposed dark mode");
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue();
                model.CanAcceptModelMemoryProposal.Should().BeFalse();
                preferences.LoadThemeMode().Should().BeNull();
                audit.Events.Should().BeEmpty();
                f.Count("reviewed_memory").Should().Be(0);
                await Click("ConfirmRegisteredSettingSuggestion");
                preferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
                audit.Events.Select(item => item.Outcome).Should()
                    .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                f.Count("reviewed_memory").Should().Be(0, "setting confirmation cannot admit memory");
                await Click("ReviewModelMemoryProposal", memory);
                model.ModelMemoryProposalDescription.Should().Contain("Prefer short answers.").And.Contain("SESSION only");
                model.CanAcceptModelMemoryProposal.Should().BeTrue();
                model.CanAdmitModelMemoryProposal.Should().BeFalse();
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                await Click("AcceptModelMemoryProposal");
                f.Count("reviewed_memory").Should().Be(0, "memory acceptance still requires separate admission");
                await Click("AdmitModelMemoryProposal");
                model.ConversationStatus.Should().Contain("SESSION memory admitted");
                model.ModelMemoryProposalDescription.Should().BeEmpty();
                f.Count("reviewed_memory").Should().Be(1);
                preferences.LoadThemeMode().Should().Be(ApplicationThemeMode.Dark);
                audit.Events.Should().HaveCount(2, "memory admission cannot repeat a setting write");
                adapter.Calls.Should().Be(2, "neither review route regenerates an answer");
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
            }
            finally { owner.Revoke(); }
        });
    }

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

    private sealed class SettingAndMemoryAnswers : IModelTurnAdapter
    {
        internal int Calls { get; private set; }

        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            Encoding.UTF8.GetString(context.Span).Should().Contain("Registered Kora setting suggestions are inert data")
                .And.Contain("responses.default-mode").And.Contain("Hybrid/VoiceOnly/VisualOnly")
                .And.Contain("including VoiceOnly")
                .And.Contain("explicitly requests a session-memory proposal");
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(Calls == 1 ? "set appearance.theme to dark" : MemoryProposal, null)));
        }
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
