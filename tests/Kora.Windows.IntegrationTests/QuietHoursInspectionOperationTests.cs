using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Configuration.SettingInspection;
using Kora.Application.Infrastructure;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.SettingInspection;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class QuietHoursInspectionOperationTests
{
    private const string Get = """{"kind":"toolCalls","calls":[{"callId":"quiet_1","capabilityId":"settings.get","input":{"id":"notices.quiet-hours"}}]}""";
    private const string Question = "Which quiet-hours schedule is configured, and why might completion speech be quiet?";

    [Theory]
    [InlineData(null, false)]
    [InlineData("Off", true)]
    [InlineData("weekly Mon,Wed,Fri 22:00 07:00 UTC", false)]
    [InlineData("weekly Mon,Tue,Wed,Thu,Fri,Sat,Sun 22:00 07:00 UTC", true)]
    public async Task NativeReviewSeparateConfirmationAndReopenedConversationCompleteConfiguredIntentAnswer(
        string? configured, bool discovery)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var answer = $"The confirmed {(configured is null ? "default" : "saved")} quiet-hours schedule is "
                + $"{configured ?? "Off"}. This is configured intent, not current quiet-now or speech consent.";
            var transport = new SettingsDiscoveryWorkflowTests.Transport(Get, JsonSerializer.Serialize(new { kind = "answer", answer }));
            await using var f = new SettingInspectionWorkflowTests.Fixture(
                transport: discovery ? transport : null, quietHours: configured is null ? null : "1\n" + configured);
            await f.InitializeAsync();
            f.Adapter.FinalAnswer = answer;
            var state = f.QuietHours.Get();
            var speechChanges = 0;
            f.QuietHours.SpeechAdmissionChanged += () => speechChanges++;
            var presenter = new SettingInspectionPresenter(f.Workflow, () => () => true, () => () => true, f.Storage.Time)
            { Question = Question, DiscoverSettings = discovery };
            var failures = new List<Exception>();
            var window = new SettingInspectionWindow(presenter, failures.Add);
            window.Show();
            try
            {
                f.Preferences.Reads.Should().Be(0);
                var textbox = window.GetVisualDescendants().OfType<TextBox>().Single();
                textbox.Text.Should().Be(Question);
                await ((AsyncCommand)Button(window, "Inspect confirmed settings and review").Command!).ExecuteAsync();
                presenter.CanConfirm.Should().BeTrue();
                presenter.ReviewText.Should().Contain("notices.quiet-hours = " + (configured ?? "Off"))
                    .And.Contain(configured is null ? "confirmed absent; live default" : "confirmed saved; matches live service")
                    .And.Contain(Question).And.Contain("NEW Local context conversation")
                    .And.Contain("configured intent only").And.Contain("not current quiet-now")
                    .And.Contain("speech consent").And.Contain("two minutes");
                f.Adapter.Calls.Should().Be(0);
                transport.Requests.Should().BeEmpty();
                f.Storage.Count("conversation_messages").Should().Be(0);
                await ((AsyncCommand)Button(window, presenter.ConfirmLabel).Command!).ExecuteAsync();
                presenter.Answer.Should().Be(answer, "native status: {0}", presenter.Status);
                presenter.CanConfirm.Should().BeFalse();
                HostRequest request;
                if (discovery)
                {
                    transport.Requests.Should().HaveCount(2);
                    transport.Requests.Should().OnlyContain(item => item.Context.UserRequest == Question
                        && item.Context.ToolMode == ModelToolMode.SettingsDiscovery
                        && item.Context.Evidence.Single().Disclosure == ModelEvidenceDisclosure.LocalOnly);
                    transport.Requests.Sum(item => ModelToolProtocol.SerializeRequest(item).Length)
                        .Should().BeLessThanOrEqualTo(ModelToolProtocol.MaximumInputUtf8Bytes);
                    using var json = JsonDocument.Parse(((ModelToolResultMessage)transport.Requests[1].Messages[1]).Result.AsMemory());
                    var observed = json.RootElement.GetProperty("settings").GetProperty("observation");
                    observed.GetProperty("quietHours").GetString().Should().Be(configured ?? "Off");
                    observed.GetProperty("isSaved").GetBoolean().Should().Be(configured is not null);
                    observed.GetProperty("revision").GetInt64().Should().Be(state.Revision);
                    observed.TryGetProperty("value", out _).Should().BeFalse();
                    request = transport.Requests[0].Provenance.Request;
                }
                else
                {
                    f.Adapter.Calls.Should().Be(1);
                    using var wire = JsonDocument.Parse(f.Adapter.Wire!);
                    wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(Question);
                    wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                    Encoding.UTF8.GetByteCount(wire.RootElement.GetRawText())
                        .Should().BeLessThanOrEqualTo(ModelContextEnvelope.MaximumInputUtf8Bytes);
                    request = f.Adapter.Request!;
                }
                f.Storage.Reopen();
                await f.Storage.Store.InitializeAsync(f.Token);
                var retained = await f.Storage.Store.ReadConversationMessageAsync(request.SessionId, request.RequestId, f.Token);
                retained.Body.Should().Be(Question);
                retained.AssistantResponse!.Body.Should().Be(answer);
                retained.Observations.Should().BeNull();
                f.QuietHours.Get().Should().BeSameAs(state);
                speechChanges.Should().Be(0);
                AssertNoEffects(f);
                failures.Should().BeEmpty();
            }
            finally { window.Close(); await presenter.CloseAsync(); }
        });
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("get")]
    [InlineData("final")]
    [InlineData("aba")]
    [InlineData("marker")]
    [InlineData("private")]
    [InlineData("expiry")]
    public async Task QuietSourceCannotDiscloseAfterChangedUnconfirmedOrRetiredAuthority(string stage)
    {
        var transport = new SettingsDiscoveryWorkflowTests.Transport(Get, """{"kind":"answer","answer":"Must not publish."}""");
        await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Desktop, HostOperation.Request);
        var session = await f.Workflow.ReviewAsync(Question, () => true, f.Token, ModelToolMode.SettingsDiscovery);
        var before = f.QuietHours.Get();
        if (stage is "confirm") { f.Preferences.Text["notices-quiet-hours.txt"] = "1\nOff"; }
        transport.AfterRequest = async _ =>
        {
            if (stage is "final" && transport.Requests.Count == 1) { return; }
            if (stage is "private") { f.Controls.CanInspect = false; }
            else if (stage is "expiry") { f.Storage.Time.Now = session.Review.ExpiresAt; }
            else if (stage is "marker") { f.Preferences.Text["notices-quiet-hours-unconfirmed.txt"] = "1"; }
            else { f.Preferences.Text["notices-quiet-hours.txt"] = "1\nOff"; }
            if (stage is "aba")
            {
                await f.QuietHours.ReadConfirmedSettingsAsync(f.Token);
                f.Preferences.Text.Remove("notices-quiet-hours.txt");
            }
        };
        var result = await f.Workflow.AnswerAsync(session, () => true, f.Token);
        result.Outcome.Should().NotBe(SettingInspectionOutcome.Succeeded);
        result.Receipt?.Message.AssistantResponse.Should().BeNull();
        transport.Requests.Should().HaveCount(stage is "confirm" ? 0 : stage is "final" ? 2 : 1);
        if (transport.Requests.Count == 1) { transport.Requests[0].Messages.Should().BeEmpty(); }
        f.QuietHours.Get().Should().BeSameAs(before);
        f.Controls.Audits.Where(item => item.ActionId is "setting.discovery.local")
            .Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        AssertNoEffects(f);
        (await f.Workflow.AnswerAsync(session, () => true, f.Token)).Outcome.Should().Be(SettingInspectionOutcome.Denied);
    }

    private static Button Button(Window window, string label) =>
        window.GetVisualDescendants().OfType<Button>().Single(item => string.Equals(item.Content?.ToString(), label, StringComparison.Ordinal));

    private static void AssertNoEffects(SettingInspectionWorkflowTests.Fixture f)
    {
        f.Preferences.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Storage.Count("scoped_grants").Should().Be(0);
        f.Storage.Count("host_questions").Should().Be(0);
        f.Controls.Audits.Should().NotContain(item => item.Category == SecurityAuditCategory.ConfigurationWrite);
        f.Host.IsQuiescent.Should().BeTrue();
    }
}
