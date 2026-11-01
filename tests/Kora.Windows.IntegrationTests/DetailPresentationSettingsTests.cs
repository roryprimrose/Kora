using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Configuration;
using Kora.Application.Presentation;
using Kora.Application.Voice;
using Kora.Core.Dependencies;
using Kora.Core.Presentation;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class DetailPresentationSettingsTests
{
    private static readonly string[] ControlNames =
        ["OfferDetailPresentation", "LinkOnlyDetailPresentation", "ConfirmDetailPresentation", "CancelDetailPresentation"];

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TimedFixtureSetupPreservesIndependentOptionalConfigurationBindings(bool composeDetails, bool composeExecution)
    {
        var context = TestContext.Current;
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetFullPath("."));
            await using var admission = new AudioControlAdmission(fixture.Interactions, fixture.Interactions, new(fixture.Tasks));
            var preferences = new Preferences();
            var details = new DetailPresentationConfigurationService(preferences, admission, fixture.Audit, TimeProvider.System);
            var executionPreferences = new LocalConversationExecutionPreferences(fixture);
            var execution = new ConversationExecutionConfigurationService(executionPreferences, admission, fixture.Audit);
            execution.Observe();
            await fixture.InitializeAsync(null, detailPresentationConfiguration: composeDetails ? details : null,
                conversationExecution: composeExecution ? execution : null);
            fixture.Main.DetailPresentationStatus.Should().Be(composeDetails
                ? details.Description : "Detail preference controls are not composed.");
            fixture.Main.ConversationExecutionStatus.Should().Be(composeExecution
                ? execution.Description : "Ordinary-conversation execution preferences unavailable.");
            preferences.Writes.Should().Be(0);
            executionPreferences.Load().Should().BeNull();
            using var controller = new SettingsWindowController(fixture.Main, NullLogger<SettingsWindowController>.Instance);
            controller.Navigate(SettingsSection.Responses, static () => true);
            fixture.Main.CanControlDetailPresentationNative.Should().Be(composeDetails);
            fixture.Main.CanChangeConversationExecution.Should().Be(composeExecution);
            if (composeDetails)
            {
                await fixture.Main.LinkOnlyDetailPresentationCommand.ExecuteAsync();
            }
            var detailReview = fixture.Main.DetailPresentationReviewText;
            if (composeExecution)
            {
                fixture.Main.SelectedConversationExecutionLimit = fixture.Main.ConversationExecutionLimits[0];
                await fixture.Main.ReviewConversationExecutionCommand.ExecuteAsync();
                executionPreferences.Load().Should().BeNull();
                await fixture.Main.ConfirmConversationExecutionCommand.ExecuteAsync();
                executionPreferences.Load()!.Value.Should().Be(1);
                fixture.Main.DetailPresentationReviewText.Should().Be(detailReview);
            }
            preferences.Writes.Should().Be(0);
            if (composeDetails)
            {
                await fixture.Main.ConfirmDetailPresentationCommand.ExecuteAsync();
                preferences.Load().Should().Be(DetailPresentationMode.LinkOnly);
            }
            preferences.Writes.Should().Be(composeDetails ? 1 : 0);
            executionPreferences.Load().Should().Be(composeExecution ? new Kora.Core.Configuration.ConversationExecutionLimit(1) : null);
            fixture.Main.HasVoiceConsent.Should().BeFalse();
            fixture.Main.IsVoiceEnabled.Should().BeFalse();
        });
        using var report = JsonDocument.Parse(context.Attachments![NativeFixturePhaseTimings.AttachmentName].AsString());
        report.RootElement.GetProperty("TestUniqueId").GetString().Should().Be(context.Test!.UniqueID);
        var phases = report.RootElement.GetProperty("PhaseStates").EnumerateArray().ToArray();
        phases.Select(phase => phase.GetProperty("Phase").GetString())
            .Should().Equal("native-ui", "owned-storage", "setup", "context", "disposal", "join", "evidence-flush");
        phases.Should().OnlyContain(phase => phase.GetProperty("Outcome").GetString() == "completed");
    }

    [Fact]
    public async Task OpenNativeDetailControlsReenableAfterBusyReadinessPublicationWithoutReopeningSettings()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."));
            await using var admission = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var preferences = new Preferences();
            var configuration = new DetailPresentationConfigurationService(preferences, admission, f.Audit, TimeProvider.System);
            var executionPreferences = new LocalConversationExecutionPreferences(f);
            var execution = new ConversationExecutionConfigurationService(executionPreferences, admission, f.Audit);
            execution.Observe();
            var probe = new DetailReadinessProbe();
            await f.InitializeAsync(null, modelProbe: probe, detailPresentationConfiguration: configuration,
                conversationExecution: execution);
            using var controller = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            controller.Navigate(SettingsSection.Responses, static () => true);
            var window = (SettingsWindow)typeof(SettingsWindowController).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
            var controls = ControlNames
                .Select(name => window.FindControl<Button>(name)!).ToArray();
            Dispatcher.UIThread.RunJobs();
            controls.Should().OnlyContain(button => button.IsEnabled);
            probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var refresh = f.Main.RefreshCommand.ExecuteAsync();
            try
            {
                f.Main.IsBusy.Should().BeTrue();
                Dispatcher.UIThread.RunJobs();
                controls.Should().OnlyContain(button => !button.IsEnabled);
            }
            finally
            {
                probe.Gate.TrySetResult();
                await refresh;
            }
            Dispatcher.UIThread.RunJobs();
            f.Main.IsBusy.Should().BeFalse();
            window.IsVisible.Should().BeTrue();
            controls.Should().OnlyContain(button => button.IsEnabled, "the final busy=false must refresh the already-open native bindings");
            await f.Main.LinkOnlyDetailPresentationCommand.ExecuteAsync();
            var detailReview = f.Main.DetailPresentationReviewText;
            f.Main.SelectedConversationExecutionLimit = f.Main.ConversationExecutionLimits[0];
            await f.Main.ReviewConversationExecutionCommand.ExecuteAsync();
            preferences.Writes.Should().Be(0);
            executionPreferences.Load().Should().BeNull();
            await f.Main.ConfirmConversationExecutionCommand.ExecuteAsync();
            executionPreferences.Load()!.Value.Should().Be(1);
            preferences.Writes.Should().Be(0);
            f.Main.DetailPresentationReviewText.Should().Be(detailReview);
            await f.Main.ConfirmDetailPresentationCommand.ExecuteAsync();
            preferences.Load().Should().Be(DetailPresentationMode.LinkOnly);
            preferences.Writes.Should().Be(1);
            executionPreferences.Load()!.Value.Should().Be(1);
            f.Main.HasVoiceConsent.Should().BeFalse();
            window.Close();
        });
    }

    [Fact]
    public async Task NativeReviewConfirmCancelResetAndHideUseSharedWorkflowWithoutAudioOrRealPreferences()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetFullPath("."));
            await using var admission = new AudioControlAdmission(f.Interactions, f.Interactions, new(f.Tasks));
            var preferences = new Preferences();
            var configuration = new DetailPresentationConfigurationService(preferences, admission, f.Audit, TimeProvider.System);
            await f.InitializeAsync(null, detailPresentationConfiguration: configuration);
            using var controller = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            f.Main.ShowSettings();
            var window = (SettingsWindow)typeof(SettingsWindowController).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
            window.FindControl<Button>("LinkOnlyDetailPresentation")!.Command.Should().BeSameAs(f.Main.LinkOnlyDetailPresentationCommand);
            window.FindControl<Button>("ConfirmDetailPresentation")!.Command.Should().BeSameAs(f.Main.ConfirmDetailPresentationCommand);
            await f.Main.InspectDetailPresentationCommand.ExecuteAsync();
            await f.Main.LinkOnlyDetailPresentationCommand.ExecuteAsync();
            preferences.Writes.Should().Be(0);
            await f.Main.CancelDetailPresentationCommand.ExecuteAsync();
            await f.Main.ConfirmDetailPresentationCommand.ExecuteAsync();
            preferences.Writes.Should().Be(0);
            await f.Main.LinkOnlyDetailPresentationCommand.ExecuteAsync();
            window.Hide();
            f.Main.ShowSettings();
            await f.Main.ConfirmDetailPresentationCommand.ExecuteAsync();
            preferences.Writes.Should().Be(0, "hidden/reopened native lifetime cannot revive its review");
            await f.Main.LinkOnlyDetailPresentationCommand.ExecuteAsync();
            await f.Main.ConfirmDetailPresentationCommand.ExecuteAsync();
            preferences.Load().Should().Be(DetailPresentationMode.LinkOnly);
            await f.Main.ResetDetailPresentationCommand.ExecuteAsync();
            preferences.Load().Should().Be(DetailPresentationMode.LinkOnly);
            await f.Main.ConfirmDetailPresentationCommand.ExecuteAsync();
            preferences.Load().Should().Be(DetailPresentationMode.Offer);
            preferences.Writes.Should().Be(2);
            f.Main.HasVoiceConsent.Should().BeFalse();
            f.Main.IsVoiceEnabled.Should().BeFalse();
            f.Access.SetOpen(false);
            await f.Main.LinkOnlyDetailPresentationCommand.ExecuteAsync();
            preferences.Writes.Should().Be(2);
            window.Close();
        });
    }

    internal sealed class Preferences : IDetailPresentationPreferences
    {
        internal DetailPresentationMode? Value { get; set; }
        internal int Writes { get; private set; }
        private bool pending;
        public DetailPresentationMode? Load() => pending ? throw new InvalidDataException() : Value;
        public DetailPresentationMode? ReadBack() => Value;
        public void BeginWrite() => pending = true;
        public void Save(DetailPresentationMode mode) { Value = mode; Writes++; }
        public void ConfirmWrite() => pending = false;
    }

    private sealed class DetailReadinessProbe : ISetupDependencyProbe
    {
        public string TaskId => "local.inference";
        public string TaskName => "Synthetic detail readiness";
        internal TaskCompletionSource? Gate { get; set; }
        public async ValueTask<DependencyStatus> ProbeAsync(CancellationToken token)
        {
            if (Gate is { } gate) { await gate.Task.WaitAsync(token); }
            return new(TaskId, TaskName, DependencyReadiness.Ready, "Synthetic ready provider");
        }
    }
}
