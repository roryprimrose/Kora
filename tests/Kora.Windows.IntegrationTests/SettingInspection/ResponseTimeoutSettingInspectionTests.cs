using System.Text.Json;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Configuration.SettingInspection;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.SettingInspection;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ResponseTimeoutSettingInspectionTests
{
    private const string Question = "Which response-window hide delay is configured, and is that the same as presence inactivity or my ordinary task deadline?";

    [Theory]
    [InlineData(null, false, RequestOrigin.LocalUi)]
    [InlineData(null, true, RequestOrigin.ActivatedVoice)]
    [InlineData(1, false, RequestOrigin.ActivatedVoice)]
    [InlineData(1, true, RequestOrigin.LocalUi)]
    [InlineData(5, false, RequestOrigin.LocalUi)]
    [InlineData(5, true, RequestOrigin.ActivatedVoice)]
    [InlineData(60, false, RequestOrigin.ActivatedVoice)]
    [InlineData(60, true, RequestOrigin.LocalUi)]
    public async Task NativeOriginalReviewSeparatelyConfirmsExactResponseDelayAndReopensRetainedAnswer(
        int? saved, bool discovery, RequestOrigin origin)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var seconds = saved ?? 5;
            var answer = $"The confirmed {(saved.HasValue ? "saved" : "default")} response-window inactivity hide delay is {seconds} seconds. "
                + "Presence inactivity is separately configured at 10 seconds. Neither is your ordinary task deadline. "
                + "These settings do not establish current visibility or whether visibility exceptions apply.";
            var final = JsonSerializer.Serialize(new { kind = "answer", answer });
            var transport = new SettingsDiscoveryWorkflowTests.Transport(discovery
                ? ["""{"kind":"toolCalls","calls":[{"callId":"response_1","capabilityId":"settings.get","input":{"id":"appearance.response-timeout"}},{"callId":"presence_1","capabilityId":"settings.get","input":{"id":"appearance.presence-timeout"}}]}""", final]
                : [final]);
            await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport, responseTimeout: saved);
            await f.InitializeAsync();
            using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
            var presenter = new SettingInspectionPresenter(f.Workflow, () => () => true, () => () => true, f.Storage.Time)
            { Question = Question, DiscoverSettings = discovery };
            var failures = new List<Exception>();
            var window = new SettingInspectionWindow(presenter, failures.Add);
            window.Show();
            try
            {
                f.Preferences.Reads.Should().Be(0);
                transport.Requests.Should().BeEmpty();
                await ((AsyncCommand)Button(window, "Inspect confirmed settings and review").Command!).ExecuteAsync();
                presenter.CanConfirm.Should().BeTrue();
                presenter.ReviewText.Should().Contain(Question).And.Contain($"appearance.response-timeout = {seconds} seconds")
                    .And.Contain(saved.HasValue ? "confirmed saved" : "confirmed absent")
                    .And.Contain("appearance.presence-timeout = 10 seconds")
                    .And.Contain("not presence inactivity or a task/provider deadline")
                    .And.Contain("no current visibility or visibility-exception claim")
                    .And.Contain("NEW Local context conversation");
                presenter.Answer.Should().BeEmpty();
                transport.Requests.Should().BeEmpty();
                f.Storage.Count("work_sessions").Should().Be(0);
                AssertNoEffects(f);
                await ((AsyncCommand)Button(window, presenter.ConfirmLabel).Command!).ExecuteAsync();
                presenter.Answer.Should().Be(answer);
                presenter.CanConfirm.Should().BeFalse();
                transport.Requests.Should().HaveCount(discovery ? 2 : 1);
                transport.Requests.Should().OnlyContain(request => string.Equals(request.Context.UserRequest, Question, StringComparison.Ordinal)
                    && request.Context.Evidence.Single().Content.Contains($"appearance.response-timeout = {seconds} seconds", StringComparison.Ordinal)
                    && request.Provenance.Request.Origin == origin
                    && request.Context.ToolMode == (discovery ? ModelToolMode.SettingsDiscovery : ModelToolMode.None));
                if (discovery)
                {
                    using var get = JsonDocument.Parse(((ModelToolResultMessage)transport.Requests[1].Messages[1]).Result.AsMemory());
                    var observed = get.RootElement.GetProperty("settings").GetProperty("observation");
                    observed.GetProperty("value").GetInt32().Should().Be(seconds);
                    observed.GetProperty("isSaved").GetBoolean().Should().Be(saved.HasValue);
                    observed.GetProperty("text").GetString().Should().Contain("configured response-window inactivity hide delay");
                    observed.GetProperty("revision").GetInt64().Should().Be(f.Appearance.Get(Kora.Core.Configuration.AppearanceOption.ResponseTimeout).Revision);
                }
                var request = transport.Requests[0].Provenance.Request;
                f.Storage.Reopen();
                await f.Storage.Store.InitializeAsync(f.Token);
                var retained = await f.Storage.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token);
                retained.Body.Should().Be(Question);
                retained.AssistantResponse!.Body.Should().Be(answer);
                retained.Observations.Should().BeNull();
                f.Storage.Count("conversation_messages").Should().Be(1);
                f.Controls.Audits.Where(item => string.Equals(item.ActionId,
                        discovery ? "setting.discovery.local" : "setting.inspection.local", StringComparison.Ordinal))
                    .Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
                AssertNoEffects(f);
                failures.Should().BeEmpty();
            }
            finally { window.Close(); await presenter.CloseAsync(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeCancellationClearsResponseReviewWithoutInferenceOrConversation(bool discovery)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new SettingInspectionWorkflowTests.Fixture(responseTimeout: 7);
            await f.InitializeAsync();
            var presenter = new SettingInspectionPresenter(f.Workflow, () => () => true, () => () => true, f.Storage.Time)
            { Question = Question, DiscoverSettings = discovery };
            var failures = new List<Exception>();
            var window = new SettingInspectionWindow(presenter, failures.Add);
            window.Show();
            try
            {
                await ((AsyncCommand)Button(window, "Inspect confirmed settings and review").Command!).ExecuteAsync();
                presenter.ReviewText.Should().Contain("appearance.response-timeout = 7 seconds");
                await ((AsyncCommand)Button(window, "Cancel and clear inspection").Command!).ExecuteAsync();
                presenter.Answer.Should().BeEmpty();
                presenter.ReviewText.Should().BeEmpty();
                presenter.CanConfirm.Should().BeFalse();
                f.Adapter.Calls.Should().Be(0);
                f.Storage.Count("work_sessions").Should().Be(0);
                f.Storage.Count("conversation_messages").Should().Be(0);
                f.Controls.Audits.Should().BeEmpty();
                AssertNoEffects(f);
                failures.Should().BeEmpty();
            }
            finally { window.Close(); await presenter.CloseAsync(); }
        });
    }

    [Theory]
    [InlineData(0, "changed")]
    [InlineData(1, "changed")]
    [InlineData(2, "changed")]
    [InlineData(1, "saved-default")]
    [InlineData(2, "malformed")]
    [InlineData(2, "revision-return")]
    [InlineData(1, "other-source")]
    public async Task CompleteResponseSourceCannotSurviveChangeAcrossGetOrFinalAwait(int phase, string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var transport = new SettingsDiscoveryWorkflowTests.Transport(
                """{"kind":"toolCalls","calls":[{"callId":"response_1","capabilityId":"settings.get","input":{"id":"appearance.response-timeout"}}]}""",
                """{"kind":"answer","answer":"This stale answer must not be published."}""");
            await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport);
            await f.InitializeAsync();
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
                HostActivityLayer.Desktop, HostOperation.Request);
            var session = await f.Workflow.ReviewAsync(Question, () => true, f.Token, ModelToolMode.SettingsDiscovery);
            session.Review.Observations.Should().HaveCount(23);
            session.Review.Observations.Single(item => item.Setting == RegisteredSetting.ResponseTimeout).Value.Should().Be(5);
            void Change()
            {
                if (string.Equals(boundary, "other-source", StringComparison.Ordinal))
                { f.Preferences.Text["presence-inactivity-timeout-seconds.txt"] = "7"; }
                else if (string.Equals(boundary, "revision-return", StringComparison.Ordinal))
                {
                    f.Preferences.Text["response-timeout-seconds.txt"] = "7";
                    f.Appearance.Reload();
                    f.Preferences.Text.Remove("response-timeout-seconds.txt");
                    f.Appearance.Reload();
                }
                else { f.Preferences.Text["response-timeout-seconds.txt"] = string.Equals(boundary, "malformed", StringComparison.Ordinal) ? "bad"
                    : string.Equals(boundary, "saved-default", StringComparison.Ordinal) ? "5" : "7"; }
            }
            if (phase == 0) { Change(); }
            transport.AfterRequest = request =>
            {
                if (request.Round == phase) { Change(); }
                return Task.CompletedTask;
            };
            var result = await f.Workflow.AnswerAsync(session, () => true, f.Token);
            result.Outcome.Should().NotBe(SettingInspectionOutcome.Succeeded);
            result.Receipt?.Message.AssistantResponse.Should().BeNull();
            transport.Requests.Should().HaveCount(phase);
            f.Controls.Audits.Should().NotContain(item => string.Equals(item.ActionId, "setting.discovery.local", StringComparison.Ordinal)
                && item.Outcome == SecurityAuditOutcome.Succeeded);
            AssertNoEffects(f);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedPhysicalVisibilityAndDeadlineQuestionRetainsOnlyGenuineUnsupportedAnswer(bool discovery)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            const string question = "Is the response window currently visible, and what is my ordinary task deadline?";
            var transport = new SettingsDiscoveryWorkflowTests.Transport(
                JsonSerializer.Serialize(new { kind = "answer", answer = SettingInspectionReview.Unsupported }));
            await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport);
            await f.InitializeAsync();
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
            var session = await f.Workflow.ReviewAsync(question, () => true, f.Token,
                discovery ? ModelToolMode.SettingsDiscovery : ModelToolMode.None);
            session.Review.ModelPolicy.Should().Contain("independent of presence inactivity and task/provider deadlines")
                .And.Contain("neither current visibility nor the absence of visibility exceptions");
            var result = await f.Workflow.AnswerAsync(session, () => true, f.Token);
            result.Outcome.Should().Be(SettingInspectionOutcome.Unsupported);
            var request = result.Receipt!.Message.Request;
            f.Storage.Reopen();
            await f.Storage.Store.InitializeAsync(f.Token);
            var retained = await f.Storage.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token);
            retained.Body.Should().Be(question);
            retained.AssistantResponse!.Body.Should().Be(SettingInspectionReview.Unsupported);
            retained.Observations.Should().BeNull();
            AssertNoEffects(f);
        });
    }

    private static void AssertNoEffects(SettingInspectionWorkflowTests.Fixture f)
    {
        f.Preferences.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Storage.Count("scoped_grants").Should().Be(0);
        f.Storage.Count("host_questions").Should().Be(0);
        f.Host.IsQuiescent.Should().BeTrue();
    }

    private static Button Button(Window window, string label) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => string.Equals(button.Content as string, label, StringComparison.Ordinal));
}
