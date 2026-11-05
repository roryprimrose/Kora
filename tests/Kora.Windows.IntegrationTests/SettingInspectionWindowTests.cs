using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Reflection;
using AwesomeAssertions;
using Kora.Application.Infrastructure;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Voice;
using Kora.SettingInspection;
using Kora.NativeUxFixture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SettingInspectionWindowTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeTimingReviewSeparatelyConfirmsExactDefaultOrSavedLocalRetainedAnswer(bool saved, bool discovery)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var answer = saved
                ? "Command start is 7.1234567 seconds, reply start is 20.7654321 seconds and trailing silence is 0.5000001 seconds, confirmed saved."
                : "Command start is 5 seconds, reply start is 15 seconds and trailing silence is 1 second, confirmed defaults.";
            var final = System.Text.Json.JsonSerializer.Serialize(new { kind = "answer", answer });
            var transport = new SettingsDiscoveryWorkflowTests.Transport(discovery
                ? ["""{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"settings.get","input":{"id":"voice.trailing-silence"}}]}""", final]
                : [final]);
            await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport,
                savedTiming: saved ? new VoiceCaptureTiming(7.1234567m, 20.7654321m, 0.5000001m) : null);
            await f.InitializeAsync();
            var presenter = new SettingInspectionPresenter(f.Workflow, () => () => true, () => () => true, f.Storage.Time)
            { Question = CaptureTimingSettingInspectionTests.Question };
            var failures = new List<Exception>();
            var window = new SettingInspectionWindow(presenter, failures.Add);
            window.Show();
            try
            {
                window.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = discovery;
                await ((AsyncCommand)Button(window, "Inspect confirmed settings and review").Command!).ExecuteAsync();
                presenter.CanConfirm.Should().BeTrue();
                presenter.ReviewText.Should().Contain(presenter.Question).And.Contain("NEW Local context conversation")
                    .And.Contain(saved ? "voice.command-start-wait = 7.1234567 seconds" : "voice.command-start-wait = 5 seconds")
                    .And.Contain(saved ? "voice.reply-start-wait = 20.7654321 seconds" : "voice.reply-start-wait = 15 seconds")
                    .And.Contain(saved ? "voice.trailing-silence = 0.5000001 seconds" : "voice.trailing-silence = 1 seconds")
                    .And.Contain(saved ? "confirmed saved" : "confirmed absent");
                transport.Requests.Should().BeEmpty();
                f.Storage.Count("conversation_messages").Should().Be(0);
                await ((AsyncCommand)Button(window, discovery
                    ? "Confirm ONE settings-discovery LocalOnly request" : "Confirm ONE tool-free LocalOnly request").Command!).ExecuteAsync();
                presenter.Answer.Should().Be(answer);
                presenter.CanConfirm.Should().BeFalse();
                f.Storage.Count("conversation_messages").Should().Be(1);
                var request = transport.Requests[0].Provenance.Request;
                f.Storage.Reopen();
                await f.Storage.Store.InitializeAsync(f.Token);
                var retained = await f.Storage.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token);
                retained.Body.Should().Be(CaptureTimingSettingInspectionTests.Question);
                retained.AssistantResponse!.Body.Should().Be(answer);
                retained.Observations.Should().BeNull();
                f.Preferences.Writes.Should().Be(0);
                f.Playback.Effects.Should().Be(0);
                f.Storage.Count("scoped_grants").Should().Be(0);
                f.Storage.Count("host_questions").Should().Be(0);
                f.Host.IsQuiescent.Should().BeTrue();
                failures.Should().BeEmpty();
            }
            finally { window.Close(); await presenter.CloseAsync(); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReentrantCancellationOrCloseJoinsTheOperationBeforeStartupNotifications(bool confirm, bool close)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new SettingInspectionWorkflowTests.Fixture();
            await f.InitializeAsync();
            var presenter = new SettingInspectionPresenter(f.Workflow, () => () => true, () => () => true, f.Storage.Time)
            { Question = "What is my reading text size?" };
            if (confirm) { await presenter.ReviewAsync(); }
            Task? retirement = null;
            bool? prematurelyJoined = null;
            presenter.PropertyChanged += (_, args) =>
            {
                if (!string.Equals(args.PropertyName, nameof(presenter.IsBusy), StringComparison.Ordinal)
                    || !presenter.IsBusy || prematurelyJoined.HasValue)
                { return; }
                prematurelyJoined = false;
                retirement = close ? presenter.CloseAsync() : presenter.CancelAsync();
                prematurelyJoined = retirement.IsCompleted;
            };

            await (confirm ? presenter.ConfirmAsync() : presenter.ReviewAsync());
            prematurelyJoined.Should().BeFalse("startup observers must join the newly owned operation, not its predecessor");
            retirement.Should().NotBeNull();
            await retirement!;
            presenter.IsBusy.Should().BeFalse();
            presenter.CanConfirm.Should().BeFalse();
            presenter.CanReview.Should().Be(!close);
            presenter.ReviewText.Should().BeEmpty();
            presenter.Answer.Should().BeEmpty();
            presenter.Status.Should().Contain("physically joined");
            f.Adapter.Calls.Should().Be(0);
            f.Storage.Count("work_sessions").Should().Be(0);
            f.Storage.Count("conversation_messages").Should().Be(0);
            f.Preferences.Writes.Should().Be(0);
            f.Playback.Effects.Should().Be(0);
            f.Host.IsQuiescent.Should().BeTrue();
            await presenter.CloseAsync();
        });
    }

    [Fact]
    public async Task ExplicitNativeDiscoveryChoiceRequiresFreshReviewAndSeparateExactConfirmation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var transport = new SettingsDiscoveryWorkflowTests.Transport(
                """{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"settings.get","input":{"id":"appearance.presence-timeout"}}]}""",
                """{"kind":"answer","answer":"The confirmed default presence timeout is 10 seconds, size is 360 pixels and movement speed is 100 percent."}""");
            await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport);
            await f.InitializeAsync();
            var presenter = new SettingInspectionPresenter(f.Workflow, () => () => true, () => () => true, f.Storage.Time)
            { Question = "Why does Kora disappear so quickly, and what presence size and movement settings am I using?" };
            var failures = new List<Exception>();
            var window = new SettingInspectionWindow(presenter, failures.Add);
            window.Show();
            try
            {
                await presenter.ReviewAsync();
                presenter.CanConfirm.Should().BeTrue();
                var checkbox = window.GetVisualDescendants().OfType<CheckBox>().Single();
                checkbox.IsChecked = true;
                presenter.DiscoverSettings.Should().BeTrue();
                presenter.CanConfirm.Should().BeFalse();
                presenter.ReviewText.Should().BeEmpty();
                transport.Requests.Should().BeEmpty();
                await ((AsyncCommand)Button(window, "Inspect confirmed settings and review").Command!).ExecuteAsync();
                presenter.ReviewText.Should().Contain("ONLY settings.list/settings.get")
                    .And.Contain("appearance.presence-timeout = 10 seconds")
                    .And.Contain("appearance.presence-size = 360 pixels")
                    .And.Contain("appearance.dot-size = 100 percent")
                    .And.Contain("appearance.dot-density = 100 percent")
                    .And.Contain("appearance.movement-speed = 100 percent")
                    .And.Contain("appearance.speech-scaling = true boolean")
                    .And.Contain("appearance.speech-scale-amount = 100 percent")
                    .And.Contain("appearance.reduced-motion = false boolean")
                    .And.Contain("value-unavailable").And.Contain(presenter.Question);
                await ((AsyncCommand)Button(window, "Confirm ONE settings-discovery LocalOnly request").Command!).ExecuteAsync();
                presenter.Answer.Should().Be(
                    "The confirmed default presence timeout is 10 seconds, size is 360 pixels and movement speed is 100 percent.");
                transport.Requests.Should().HaveCount(2);
                f.Preferences.Writes.Should().Be(0);
                f.Playback.Effects.Should().Be(0);
                checkbox.IsChecked = false;
                presenter.Answer.Should().BeEmpty();
                presenter.ReviewText.Should().BeEmpty();
                presenter.CanConfirm.Should().BeFalse();
                failures.Should().BeEmpty();
            }
            finally { window.Close(); await presenter.CloseAsync(); }
        });
    }

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
                    .And.Contain("appearance.presence-display = true boolean (confirmed absent; live default)")
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
