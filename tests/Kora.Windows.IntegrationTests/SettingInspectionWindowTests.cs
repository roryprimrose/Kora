using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Reflection;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Core.Configuration.SettingInspection;
using Kora.SettingInspection;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingInspectionWindowTests
{
    [Fact]
    public async Task NativeCloseHidesImmediatelyAndOnlyClosesAfterActualIgnoringModelJoin()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new SettingInspectionWorkflowTests.Fixture();
            await f.InitializeAsync();
            var presenter = new SettingInspectionPresenter(f.Workflow, () => () => true, () => () => true, f.Storage.Time)
            { Question = "What is my reading text size?" };
            var failures = new List<Exception>();
            var window = new SettingInspectionWindow(presenter, failures.Add);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            await presenter.ReviewAsync();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Adapter.Hold = () => release.Task;
            var work = presenter.ConfirmAsync();
            await f.Adapter.Entered.Task.WaitAsync(f.Token);
            window.Close();
            window.IsVisible.Should().BeFalse();
            closed.Task.IsCompleted.Should().BeFalse();
            presenter.IsBusy.Should().BeTrue();
            work.IsCompleted.Should().BeFalse();
            window.Close();
            closed.Task.IsCompleted.Should().BeFalse();
            release.SetResult();
            await work;
            await closed.Task.WaitAsync(f.Token);
            presenter.Answer.Should().BeEmpty();
            presenter.Question.Should().BeEmpty();
            f.Host.IsQuiescent.Should().BeTrue();
            failures.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task SettingsTypedBindingOpensReadFreeThenCompletesTheActualExplicitOperation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new SettingInspectionWorkflowTests.Fixture();
            await f.InitializeAsync();
            using var native = new NativeUxFixtureSession(f.Storage.Paths.LocalRoot);
            await native.InitializeAsync();
            using var controller = new SettingsWindowController(native.Main, NullLogger<SettingsWindowController>.Instance);
            controller.BindSettingInspection(f.Workflow, f.Storage.Time);
            native.Main.ShowSettings();
            var settings = (SettingsWindow)typeof(SettingsWindowController).GetField("window",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            var control = settings.FindControl<SettingInspectionControl>("SettingInspection")!;
            var open = (Button)control.Content!;
            open.IsEnabled.Should().BeTrue();
            open.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            var inspection = (SettingInspectionWindow)typeof(SettingInspectionControl).GetField("window",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(control)!;
            var presenter = (SettingInspectionPresenter)inspection.DataContext!;
            try
            {
                f.Preferences.Reads.Should().Be(0);
                inspection.IsVisible.Should().BeTrue();
                inspection.GetVisualDescendants().OfType<TextBox>().Single().Text = "What is my reading text size?";
                await ((AsyncCommand)Button(inspection, "Inspect confirmed settings and review").Command!).ExecuteAsync();
                presenter.ReviewText.Should().Contain("125").And.Contain("NEW Local context conversation");
                f.Adapter.Calls.Should().Be(0);
                await ((AsyncCommand)Button(inspection, "Confirm ONE tool-free LocalOnly request").Command!).ExecuteAsync();
                presenter.Answer.Should().Be(SettingInspectionWorkflowTests.AdapterData.Answer);
                f.Storage.Count("conversation_messages").Should().Be(1);
                settings.Hide();
                presenter.RefreshAdmission();
                presenter.Answer.Should().BeEmpty();
                presenter.ReviewText.Should().BeEmpty();
            }
            finally { inspection.Close(); await presenter.CloseAsync(); }
        });
    }

    [Fact]
    public async Task ActualNativeFullReviewSeparateConsentActualNoToolsLocalEnvelopeAndRetainedAnswer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new SettingInspectionWorkflowTests.Fixture();
            await f.InitializeAsync();
            var presenter = new SettingInspectionPresenter(f.Workflow, () => () => true, () => () => true, f.Storage.Time);
            var failures = new List<Exception>();
            var window = new SettingInspectionWindow(presenter, failures.Add);
            window.Show();
            try
            {
                f.Preferences.Reads.Should().Be(0);
                f.Adapter.Calls.Should().Be(0);
                var question = window.GetVisualDescendants().OfType<TextBox>().Single();
                question.Text = "What is my reading text size?";
                presenter.Question.Should().Be(question.Text);
                f.Preferences.Reads.Should().Be(0, "editing is not source capture");
                presenter.CanConfirm.Should().BeFalse();
                await ((AsyncCommand)Button(window, "Inspect confirmed settings and review").Command!).ExecuteAsync();
                presenter.ReviewText.Should().Contain("125").And.Contain("confirmed saved")
                    .And.Contain("confirmed absent").And.Contain(question.Text)
                    .And.Contain(SettingInspectionReview.Policy).And.Contain("NEW Local context conversation");
                window.GetVisualDescendants().OfType<TextBlock>().Single(control => string.Equals(
                    AutomationProperties.GetName(control), "Complete confirmed source provenance question and local target", StringComparison.Ordinal))
                    .Text.Should().Be(presenter.ReviewText);
                f.Adapter.Calls.Should().Be(0);
                f.Storage.Count("conversation_messages").Should().Be(0);
                await ((AsyncCommand)Button(window, "Confirm ONE tool-free LocalOnly request").Command!).ExecuteAsync();
                presenter.Status.Should().Contain("Succeeded").And.Contain("Exact conversation/task receipt confirmed");
                presenter.Answer.Should().Be(SettingInspectionWorkflowTests.AdapterData.Answer);
                presenter.CanConfirm.Should().BeFalse();
                f.Adapter.Calls.Should().Be(1);
                f.Storage.Count("conversation_messages").Should().Be(1);
                f.Playback.Effects.Should().Be(0);
                f.Preferences.Writes.Should().Be(0);
                failures.Should().BeEmpty();
                question.Text = "Changed";
                presenter.ReviewText.Should().BeEmpty();
                presenter.Answer.Should().BeEmpty();
            }
            finally { window.Close(); await presenter.CloseAsync(); }
        });
    }

    [Theory]
    [InlineData("question")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    [InlineData("close")]
    public async Task NativeRevocationImmediatelyClosesAuthorityButWaitsForIgnoringActualModel(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new SettingInspectionWorkflowTests.Fixture();
            await f.InitializeAsync();
            var host = true;
            var presenter = new SettingInspectionPresenter(f.Workflow, () => () => host, () => () => true, f.Storage.Time)
            { Question = "What is my reading text size?" };
            await presenter.ReviewAsync();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Adapter.Hold = () => release.Task;
            var work = presenter.ConfirmAsync();
            await f.Adapter.Entered.Task.WaitAsync(f.Token);
            Task? cancelled = null;
            switch (change)
            {
                case "question": presenter.Question = "Changed"; break;
                case "owner": host = false; break;
                case "privacy": f.Controls.CanInspect = false; break;
                case "expiry": f.Storage.Time.Now = f.Storage.Time.Now.AddMinutes(3); break;
                case "cancel": cancelled = presenter.CancelAsync(); break;
                case "close": cancelled = presenter.CloseAsync(); break;
            }
            presenter.RefreshAdmission();
            presenter.Answer.Should().BeEmpty();
            presenter.ReviewText.Should().BeEmpty();
            work.IsCompleted.Should().BeFalse();
            presenter.IsBusy.Should().BeTrue();
            if (cancelled is not null) { cancelled.IsCompleted.Should().BeFalse(); }
            release.SetResult();
            await work;
            if (cancelled is not null) { await cancelled; }
            presenter.IsBusy.Should().BeFalse();
            presenter.Answer.Should().BeEmpty();
            f.Host.IsQuiescent.Should().BeTrue();
            f.Adapter.Calls.Should().Be(1);
            f.Controls.Audits.Where(item => string.Equals(item.ActionId, "setting.inspection.local", StringComparison.Ordinal))
                .Should().NotContain(item => item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
            await presenter.CloseAsync();
        });
    }

    private static Button Button(Window window, string label) =>
        window.GetVisualDescendants().OfType<Button>().Single(control => string.Equals(control.Content as string, label, StringComparison.Ordinal));
}
