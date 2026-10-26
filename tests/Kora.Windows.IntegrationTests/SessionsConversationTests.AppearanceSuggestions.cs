using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
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
    public async Task AppearanceSuggestionNativeRoutesReviewExactAnswerAndRequireSeparateAuditedConfirmation(bool compact, bool reader)
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
            var adapter = new AppearanceAnswerAdapter(reader);
            await using var composed = new Composition(f, adapter: adapter, appearance: appearance);
            var model = composed.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest a more readable appearance.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            answer.HasAssistantResponse.Should().BeTrue();
            adapter.Calls.Should().Be(1);
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                var option = reader ? AppearanceOption.ReaderTextScale : AppearanceOption.Theme;
                var before = appearance.Get(option);
                appearance.Get(option).Value.Should().Be(before.Value);
                audit.Events.Should().BeEmpty("model completion cannot write or approve an appearance change");
                var reviewButton = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Name, "ReviewRegisteredSettingSuggestion", StringComparison.Ordinal));
                reviewButton.DataContext.Should().BeSameAs(answer);
                reviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.RegisteredSettingsDescription.Should().Contain("appearance.theme").And.Contain("native review only");
                model.RegisteredSettingSuggestionDescription.Should().Be(model.AppearanceSuggestionDescription);
                model.AppearanceSuggestionDescription.Should()
                    .Contain(reader ? "current 100 (default); proposed 125 percent" : "current system (default); proposed dark mode")
                    .And.Contain("shared device-local");
                appearance.Get(option).Should().Be(before);
                audit.Events.Should().BeEmpty();
                window.FindControl<Button>("CancelRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                appearance.Get(option).Should().Be(before);
                audit.Events.Should().BeEmpty();
                model.ConversationMessages.Should().Contain(row => ReferenceEquals(row, answer));
                await model.ReviewRegisteredSettingSuggestionAsync(answer);
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue(model.ConversationStatus);
                window.FindControl<Button>("ConfirmRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.ConversationStatus.Should().NotContain("Unconfirmed");
                appearance.Get(option).Value.Should().Be(reader ? new AppearanceValue.Number(125)
                    : new AppearanceValue.Theme(ApplicationThemeMode.Dark));
                var restored = new AppearanceConfigurationService(preferences, audit,
                    NullLogger<AppearanceConfigurationService>.Instance);
                restored.Get(option).Value.Should().Be(appearance.Get(option).Value);
                audit.Events.Select(item => item.Outcome).Should()
                    .Equal(Kora.Core.Auditing.SecurityAuditOutcome.Requested, Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
                model.AppearanceSuggestionDescription.Should().BeEmpty();
                model.CanConfirmAppearanceSuggestion.Should().BeFalse();
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                adapter.Calls.Should().Be(1, "review and confirmation never call a model again");
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token))
                    .Should().Be(answer, "the setting result is not fabricated assistant output");
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("close")]
    public async Task AppearanceSuggestionNativeLifecycleRevokesWithoutWritesOrLateRetargeting(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var audit = new AuditLog();
        var appearance = new AppearanceConfigurationService(
            new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths), NullLogger<LocalAppearancePreferences>.Instance),
            audit, NullLogger<AppearanceConfigurationService>.Instance);
        await using var composed = new Composition(f, adapter: new AppearanceAnswerAdapter(false), appearance: appearance);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Suggest a theme.";
        await model.SubmitConversationAsync();
        var answer = model.ConversationMessages.Single();
        await model.ReviewAppearanceSuggestionAsync(answer with { });
        model.CanConfirmAppearanceSuggestion.Should().BeFalse("reconstructed records are not native source rows");
        await model.ReviewAppearanceSuggestionAsync(answer);
        model.CanConfirmAppearanceSuggestion.Should().BeTrue();
        switch (change)
        {
            case "cancel": model.CancelAppearanceSuggestion(); break;
            case "source": await model.ReadConversationAsync(); break;
            case "private": composed.Access.CanInspect = false; break;
            case "close": model.Close(); break;
        }
        model.AppearanceSuggestionDescription.Should().BeEmpty();
        model.CanConfirmAppearanceSuggestion.Should().BeFalse();
        await model.ConfirmAppearanceSuggestionAsync();
        audit.Events.Should().BeEmpty();
        appearance.Get(AppearanceOption.Theme).Value.Should().Be(new AppearanceValue.Theme(ApplicationThemeMode.System));
    }

    private sealed class AppearanceAnswerAdapter(bool reader) : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            Encoding.UTF8.GetString(context.Span).Should().Contain("one inert suggestion");
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(reader ? "set appearance.reader-text-scale to 125" : "set appearance.theme to dark", null)));
        }
    }
}
