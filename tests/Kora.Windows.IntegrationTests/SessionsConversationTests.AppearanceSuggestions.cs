using System.Text;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(AppearanceOption.MovementSpeed, "50")]
    [InlineData(AppearanceOption.PresenceDisplay, "false")]
    [InlineData(AppearanceOption.ResponseTimeout, "15")]
    public async Task RetainedPresenceAnswerNativeConfirmationReachesActualMainConsumerAndReopenedPreferences(
        AppearanceOption option, string value)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            using var native = new Kora.NativeUxFixture.NativeUxFixtureSession(f.Paths.LocalRoot);
            await native.InitializeAsync(null, callState: new SummaryCallState());
            var descriptor = AppearanceOptionRegistry.Get(option);
            var adapter = new AppearanceAnswerAdapter($"set {descriptor.Id} to {value}");
            await using var composed = new Composition(f, adapter: adapter);
            var model = new SessionsViewModel(composed.Workspace, native.Evidence, composed.Access,
                NullLogger<SessionsViewModel>.Instance, conversation: composed.Conversation,
                reviewAppearanceSuggestion: native.Main.ReviewAppearanceSuggestionAsync,
                confirmAppearanceSuggestion: native.Main.ConfirmAppearanceSuggestionAsync);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest a less distracting presence.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            var owner = new SessionsPresentationOwner(model);
            var window = new SessionsWindow(model, owner);
            window.Show();
            try
            {
                var preferences = new LocalAppearancePreferences(native, NullLogger<LocalAppearancePreferences>.Instance);
                var restored = new AppearanceConfigurationService(preferences, new AuditLog(),
                    NullLogger<AppearanceConfigurationService>.Instance);
                restored.Get(option).IsSaved.Should().BeFalse();
                var review = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Name, "ReviewRegisteredSettingSuggestion", StringComparison.Ordinal));
                review.DataContext.Should().BeSameAs(answer);
                review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue(model.ConversationStatus);
                model.RegisteredSettingSuggestionDescription.Should()
                    .Contain($"current {AppearanceCommand.Format(descriptor.Default)} (default); proposed {value} {descriptor.Units}")
                    .And.Contain("shared device-local");
                restored.Reload();
                restored.Get(option).IsSaved.Should().BeFalse("native Review cannot write");
                window.FindControl<Button>("ConfirmRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                Dispatcher.UIThread.RunJobs();
                model.ConversationStatus.Should().Be("Appearance setting saved on this device.");
                if (option == AppearanceOption.MovementSpeed)
                { native.Main.PresenceMovementSpeedPercent.Should().Be(50); }
                else if (option == AppearanceOption.ResponseTimeout)
                { native.Main.ResponseTimeoutSeconds.Should().Be(15); }
                else
                {
                    native.Main.IsPresenceDisplayEnabled.Should().BeFalse();
                    native.Main.PresenceDisplayDescription.Should().Contain("Hide the animated dots");
                }
                var reopened = new AppearanceConfigurationService(
                    new LocalAppearancePreferences(native, NullLogger<LocalAppearancePreferences>.Instance),
                    new AuditLog(), NullLogger<AppearanceConfigurationService>.Instance);
                AppearanceCommand.Format(reopened.Get(option).Value).Should().Be(value);
                reopened.Get(option).IsSaved.Should().BeTrue();
                foreach (var other in AppearanceOptionRegistry.Options.Where(item => item.Option != option))
                {
                    reopened.Get(other.Option).IsSaved.Should().BeFalse();
                    reopened.Get(other.Option).Value.Should().Be(other.Default);
                }
                await model.ReviewRegisteredSettingSuggestionAsync(answer);
                model.RegisteredSettingSuggestionDescription.Should().Contain($"current {value} (saved)");
                model.CancelRegisteredSettingSuggestion();
                adapter.Calls.Should().Be(1);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token))
                    .Should().Be(answer);
            }
            finally
            {
                owner.Revoke();
                await native.Main.DisposeAsync();
            }
        });
    }

    [Theory]
    [InlineData("set appearance.presence-display to 1")]
    [InlineData("set appearance.speech-scaling to light")]
    [InlineData("set appearance.presence-timeout to 61")]
    [InlineData("set appearance.presence-size to 239")]
    [InlineData("set appearance.dot-size to 201")]
    [InlineData("set appearance.dot-density to 0")]
    [InlineData("set appearance.movement-speed to false")]
    [InlineData("set appearance.movement-speed to 050")]
    [InlineData("set appearance.speech-scale-amount to -1")]
    [InlineData("set appearance.movement-speed to 50\nset appearance.dot-size to 50")]
    [InlineData("I saved appearance.movement-speed to 50")]
    [InlineData("set appearance.native-presence-renderer to true")]
    [InlineData("set appearance.unknown to 1")]
    public async Task NativePresenceReviewRejectsMalformedAnswersWithoutWriteOrOtherEffects(string text)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var audit = new AuditLog();
        var appearance = new AppearanceConfigurationService(
            new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths), NullLogger<LocalAppearancePreferences>.Instance),
            audit, NullLogger<AppearanceConfigurationService>.Instance);
        var adapter = new AppearanceAnswerAdapter(text);
        await using var composed = new Composition(f, adapter: adapter, appearance: appearance);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Suggest a presence change.";
        await model.SubmitConversationAsync();
        var source = model.ConversationMessages.Single();
        await model.ReviewRegisteredSettingSuggestionAsync(source);
        model.ConversationStatus.Should().Contain("No admitted setting suggestion");
        model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
        await model.ConfirmRegisteredSettingSuggestionAsync();
        foreach (var descriptor in AppearanceOptionRegistry.Options)
        {
            appearance.Get(descriptor.Option).IsSaved.Should().BeFalse();
            appearance.Get(descriptor.Option).Value.Should().Be(descriptor.Default);
        }
        audit.Events.Should().BeEmpty();
        adapter.Calls.Should().Be(1);
        f.Count("scoped_grants").Should().Be(0);
        f.Count("host_questions").Should().Be(0);
        (await f.Store.ReadConversationMessageAsync(source.Request.SessionId, source.Request.RequestId, f.Token))
            .Should().Be(source);
    }

    public static TheoryData<bool, AppearanceOption, string> AppearanceSuggestionNativeCases
    {
        get
        {
            var data = new TheoryData<bool, AppearanceOption, string>();
            foreach (var compact in new[] { false, true })
            foreach (var registered in RegisteredSettingRegistry.Settings.Where(item =>
                item.Id.StartsWith("appearance.", StringComparison.Ordinal)))
            {
                var descriptor = AppearanceOptionRegistry.Options.Single(item => string.Equals(item.Id, registered.Id, StringComparison.Ordinal));
                string[] values = descriptor.Type switch
                {
                    AppearanceValueType.Theme => ["light", "dark"],
                    AppearanceValueType.Toggle => ["true", "false"],
                    _ => [descriptor.Minimum!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        descriptor.Maximum!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                };
                foreach (var value in values) { data.Add(compact, descriptor.Option, value); }
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(AppearanceSuggestionNativeCases))]
    public async Task AppearanceSuggestionNativeRoutesReviewExactAnswerAndRequireSeparateAuditedConfirmation(
        bool compact, AppearanceOption option, string value)
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
            var descriptor = AppearanceOptionRegistry.Get(option);
            var adapter = new AppearanceAnswerAdapter($"set {descriptor.Id} to {value}");
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
                var before = appearance.Get(option);
                var changesValue = !string.Equals(AppearanceCommand.Format(before.Value), value, StringComparison.Ordinal);
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
                    .Contain($"current {AppearanceCommand.Format(before.Value)} (default); proposed {value} {descriptor.Units}")
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
                AppearanceCommand.Format(appearance.Get(option).Value).Should().Be(value);
                var restored = new AppearanceConfigurationService(
                    new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths), NullLogger<LocalAppearancePreferences>.Instance), audit,
                    NullLogger<AppearanceConfigurationService>.Instance);
                restored.Get(option).Value.Should().Be(appearance.Get(option).Value);
                foreach (var item in AppearanceOptionRegistry.Options)
                {
                    restored.Get(item.Option).IsSaved.Should().Be(item.Option == option && changesValue);
                    if (item.Option != option) { restored.Get(item.Option).Value.Should().Be(item.Default); }
                }
                if (changesValue)
                {
                    audit.Events.Select(item => item.Outcome).Should()
                        .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                }
                else { audit.Events.Should().BeEmpty("confirming a current default is an unchanged operation, not a save"); }
                model.AppearanceSuggestionDescription.Should().BeEmpty();
                model.CanConfirmAppearanceSuggestion.Should().BeFalse();
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                adapter.Calls.Should().Be(1, "review and confirmation never call a model again");
                await model.ConfirmRegisteredSettingSuggestionAsync();
                audit.Events.Should().HaveCount(changesValue ? 2 : 0, "the consumed native confirmation cannot be replayed");
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
    [InlineData("settings")]
    [InlineData("delete")]
    [InlineData("control")]
    public async Task AppearanceSuggestionNativeLifecycleRevokesWithoutWritesOrLateRetargeting(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var audit = new AuditLog();
        var appearance = new AppearanceConfigurationService(
            new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths), NullLogger<LocalAppearancePreferences>.Instance),
            audit, NullLogger<AppearanceConfigurationService>.Instance);
        var adapter = new AppearanceAnswerAdapter("set appearance.movement-speed to 50");
        await using var composed = new Composition(f, adapter: adapter, appearance: appearance);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        await model.ReadConversationAsync();
        model.ComposerBody = "Suggest a less distracting presence.";
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
            case "control": composed.Access.CanControl = false; break;
            case "settings":
                new LocalAppearancePreferences(new LocalPreferenceStore(f.Paths), NullLogger<LocalAppearancePreferences>.Instance)
                    .SavePresenceMovementSpeedPercent(75);
                appearance.Reload();
                break;
            case "delete":
                var request = new HostRequest(new(Guid.NewGuid()), answer.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
                using (HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request))
                {
                    var preview = await f.Store.PreviewDispositionAsync(answer.Request.SessionId, answer.Generation, 0, f.Token);
                    await f.Tasks.CommitAsync(new(request, new(1), HostTaskState.IntentRecorded), 0, f.Token);
                    await f.Store.DisposeSessionAsync(request, preview, () => true, f.Token);
                }
                break;
        }
        await model.ConfirmAppearanceSuggestionAsync();
        model.AppearanceSuggestionDescription.Should().BeEmpty();
        model.CanConfirmAppearanceSuggestion.Should().BeFalse();
        await model.ConfirmAppearanceSuggestionAsync();
        audit.Events.Should().BeEmpty();
        appearance.Get(AppearanceOption.MovementSpeed).Value.Should()
            .Be(new AppearanceValue.Number(change is "settings" ? 75 : PresenceSettings.DefaultMovementSpeedPercent));
        adapter.Calls.Should().Be(1);
    }

    private sealed class AppearanceAnswerAdapter(string answer) : IModelTurnAdapter
    {
        internal AppearanceAnswerAdapter(bool reader)
            : this(reader ? "set appearance.reader-text-scale to 125" : "set appearance.theme to dark") { }

        internal int Calls { get; private set; }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            Encoding.UTF8.GetString(context.Span).Should().Contain("one inert suggestion");
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new(answer, null)));
        }
    }
}
