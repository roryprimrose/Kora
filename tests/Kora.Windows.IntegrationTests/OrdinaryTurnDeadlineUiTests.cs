using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Application.Voice;
using Kora.Controls;
using Kora.Core.Configuration;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class OrdinaryTurnDeadlineUiTests
{
    [Fact]
    public async Task NativeDeadlineFieldSavesReadsBackAndResetsThroughAdmittedDurablePreference()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var f = NativeUxTestFixture.Create(parent);
            await using var admission = new AudioControlAdmission(f.Interactions, f.Interactions, new HostTaskCoordinator(f.Tasks));
            var preferences = new LocalOrdinaryTurnDeadlinePreferences(f);
            var service = new OrdinaryTurnDeadlineConfigurationService(preferences, admission, f.Audit);
            await f.InitializeAsync(null, ordinaryDeadlineConfiguration: service);
            var window = new SettingsWindow(f.Main);
            window.Show();
            window.ShowSection(SettingsSection.Sessions);
            f.Main.BindOrdinaryDeadlineNativeLifetime(() => true);
            try
            {
                await f.Main.RefreshOrdinaryDeadlineCommand.ExecuteAsync();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var field = window.FindControl<SettingsNumberField>("OrdinaryDeadlineField")!;
                field.IsEffectivelyVisible.Should().BeTrue();
                var input = field.FindControl<TextBox>("NumberInput")!;
                input.Text = "1";
                window.UpdateLayout();
                f.Main.SelectedOrdinaryDeadlineMinutes.Should().Be(1);
                preferences.Load().Should().BeNull("editing is not saving");
                await f.Main.SaveOrdinaryDeadlineCommand.ExecuteAsync();
                Dispatcher.UIThread.RunJobs();
                preferences.Load().Should().Be(new OrdinaryTurnDeadline(1));
                f.Main.SavedOrdinaryDeadlineMinutes.Should().Be(1);
                new LocalOrdinaryTurnDeadlinePreferences(f).Load().Should().Be(new OrdinaryTurnDeadline(1));
                await f.Main.ResetOrdinaryDeadlineCommand.ExecuteAsync();
                preferences.Load().Should().BeNull();
                f.Main.SavedOrdinaryDeadlineMinutes.Should().Be(5);
            }
            finally
            {
                f.Main.BindOrdinaryDeadlineNativeLifetime(() => false);
                window.Close();
            }
        });
    }
}
