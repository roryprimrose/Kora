using Avalonia.Controls;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Providers;
using Kora.NativeUxFixture;
using Kora.Windows.Dependencies;
using Kora.Windows.IntegrationTests.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingsOllamaEvaluationTests
{
    [Fact]
    public async Task Detached_Settings_has_explicitly_unavailable_evaluation_without_ambient_services()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await f.InitializeAsync();
            var window = new SettingsWindow(f.Main);
            try
            {
                window.FindControl<StackPanel>("LocalEvaluationControls")!.IsEnabled.Should().BeFalse();
                window.FindControl<Button>("ReviewLocalEvaluation")!.Command.Should().BeNull();
                window.FindControl<TextBlock>("LocalEvaluationStatus")!.Text.Should().Contain("unavailable");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task Native_controls_complete_distinct_inspection_confirmation_and_audited_revocation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await using var host = await OllamaAdmissionWorkflowTests.Fixture.Create();
            var observer = new Observer(host.Subject);
            await using var source = new WindowsOllamaEvaluationSource(observer, host.Storage.Time, host,
                NullLogger<WindowsOllamaEvaluationSource>.Instance);
            await using var copilot = UnavailableHosted(host);
            var profiles = Profiles(host, source, observer, copilot);
            await f.InitializeAsync(null, providerProfileConfiguration: profiles);
            var owner = new Window();
            SettingsView? view = null;
            using var controller = new SettingsWindowController(f.Main, NullLogger<SettingsWindowController>.Instance);
            controller.BindLocalEvaluation(source, host.Admission);
            controller.BindWorkbench(() => owner, shown => { view = shown; owner.Content = shown; owner.UpdateLayout(); });
            owner.Show();
            HeadlessSession.SetActivation(owner, true);
            try
            {
                controller.ShowFilter("providers.app.profile", static () => true);
                var shown = view ?? throw new InvalidOperationException("The exact native Settings view was not presented.");
                var inspect = (AsyncCommand)shown.FindControl<Button>("ReviewLocalEvaluation")!.Command!;
                var approve = (AsyncCommand)shown.FindControl<Button>("ConfirmLocalEvaluation")!.Command!;
                var revoke = (AsyncCommand)shown.FindControl<Button>("RevokeLocalEvaluation")!.Command!;
                var consent = shown.FindControl<CheckBox>("LocalEvaluationConsent")!;
                await inspect.ExecuteAsync();
                source.HasAdmission.Should().BeFalse();
                observer.Observations.Should().Be(1);
                shown.FindControl<TextBlock>("LocalEvaluationStatus")!.Text.Should().Contain("Review before allowing inference");
                consent.IsChecked.Should().BeFalse();
                consent.IsChecked = true;
                await approve.ExecuteAsync();
                source.HasAdmission.Should().BeTrue(shown.FindControl<TextBlock>("LocalEvaluationStatus")!.Text);
                observer.Checks.Should().Be(1);
                shown.FindControl<TextBlock>("LocalEvaluationStatus")!.Text.Should().Contain("No inference ran during setup");
                host.Audits.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                await f.Main.RefreshProviderProfileCommand.ExecuteAsync();
                f.Main.ResponseBody.Should().Contain("Observed; no execution authority", f.Main.Transcript);
                f.Main.SelectedProviderServiceChoice = f.Main.ProviderServiceChoices.Single(choice => choice.Service == ProviderService.Local);
                f.Main.SelectedProviderProfileModel = f.Main.ProviderProfileModels.Single();
                f.Main.SelectedProviderReasoning = f.Main.ProviderReasoningChoices.Single();
                f.Main.SelectedProviderContext = f.Main.ProviderContextChoices.Single();
                await f.Main.SaveProviderProfileCommand.ExecuteAsync();
                profiles.Get().Desired!.For(ProviderService.Local)!.Model.Should().Be(new ProviderChoiceId(WindowsOllamaSetupService.Model));
                await revoke.ExecuteAsync();
                source.HasAdmission.Should().BeFalse();
                host.Audits.TakeLast(2).Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                host.Store.Values.Keys.Should().ContainSingle().Which.Should().Be("provider-profiles.json");
            }
            finally { owner.Close(); }
        });
    }

    [Theory]
    [InlineData("close")]
    [InlineData("consent")]
    public async Task Retired_confirmation_never_revives_and_waits_for_the_actual_metadata_check(string loss)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new NativeUxFixtureSession(Path.GetTempPath());
            await using var host = await OllamaAdmissionWorkflowTests.Fixture.Create();
            var observer = new Observer(host.Subject);
            await using var source = new WindowsOllamaEvaluationSource(observer, host.Storage.Time, host,
                NullLogger<WindowsOllamaEvaluationSource>.Instance);
            await using var copilot = UnavailableHosted(host);
            await f.InitializeAsync(null, providerProfileConfiguration: Profiles(host, source, observer, copilot));
            var window = new SettingsWindow(f.Main);
            window.View.BindLocalEvaluation(source, host.Admission, NullLogger<SettingsWindowController>.Instance);
            window.Show();
            HeadlessSession.SetActivation(window, true);
            try
            {
                window.ShowFilter("providers.app.profile");
                window.UpdateLayout();
                await ((AsyncCommand)window.FindControl<Button>("ReviewLocalEvaluation")!.Command!).ExecuteAsync();
                window.FindControl<CheckBox>("LocalEvaluationConsent")!.IsChecked = true;
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                observer.OnCheck = async _ => { entered.SetResult(); await release.Task; };
                var confirming = ((AsyncCommand)window.FindControl<Button>("ConfirmLocalEvaluation")!.Command!).ExecuteAsync();
                try
                {
                    await Task.WhenAny(entered.Task, confirming).WaitAsync(f.Token);
                    entered.Task.IsCompleted.Should().BeTrue(window.FindControl<TextBlock>("LocalEvaluationStatus")!.Text);
                    if (loss is "close") { window.View.PrepareClose().Should().BeFalse(); }
                    else
                    {
                        window.FindControl<CheckBox>("LocalEvaluationConsent")!.IsChecked = false;
                        window.FindControl<CheckBox>("LocalEvaluationConsent")!.IsChecked = true;
                    }
                    confirming.IsCompleted.Should().BeFalse();
                    source.IsQuiescent.Should().BeFalse();
                    source.HasAdmission.Should().BeFalse();
                }
                finally { release.SetResult(); }
                if (loss is "close") { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => confirming); }
                else { await Assert.ThrowsAsync<InvalidOperationException>(() => confirming); }
                source.IsQuiescent.Should().BeTrue();
                source.HasAdmission.Should().BeFalse();
                window.View.PrepareClose().Should().BeTrue();
                host.Audits.Should().BeEmpty();
            }
            finally { window.Close(); }
        });
    }

    private static ProviderProfileConfigurationService Profiles(OllamaAdmissionWorkflowTests.Fixture host,
        WindowsOllamaEvaluationSource source, Observer observer, CopilotAccountBroker copilot) =>
        new(new LocalProviderProfilePreferences(host.Store),
            new CopilotProviderCatalogue(new WindowsOllamaRuntimeResolver(source, observer, host.Storage.Time,
                NullLogger<WindowsOllamaRuntimeResolver>.Instance, (_, token) =>
                { token.ThrowIfCancellationRequested(); return Task.FromResult(true); }), copilot),
            host.Admission, host, host.Storage.Time);

    private static CopilotAccountBroker UnavailableHosted(OllamaAdmissionWorkflowTests.Fixture host) =>
        new(new UnavailableCopilotDeploymentApprovalSource(), host.Storage.Time, host,
            _ => throw new InvalidOperationException("Local evaluation must not start a hosted client."),
            NullLogger<CopilotAccountBroker>.Instance);

    private sealed class Observer(WindowsOllamaAdmissionSubject subject)
        : IWindowsOllamaEvaluationObservation, ILocalProviderInstallationObservation
    {
        internal int Observations { get; private set; }
        internal int Checks { get; private set; }
        internal Func<CancellationToken, Task>? OnCheck { get; set; }
        public Task<WindowsOllamaAdmissionSubject?> ObserveEvaluationAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Observations++;
            return Task.FromResult<WindowsOllamaAdmissionSubject?>(subject);
        }
        public async Task<bool> IsEvaluationCurrentAsync(WindowsOllamaAdmissionSubject reviewed, CancellationToken cancellationToken)
        {
            Checks++;
            if (OnCheck is not null) { await OnCheck(cancellationToken); }
            return reviewed == subject;
        }
        public Task<LocalProviderInstallation> ObserveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(LocalProviderInstallation.Installed);
    }
}
