using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Auditing;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class CaptureTimingSettingInspectionTests
{
    internal const string Question = "Why does capture stop or wait, and which command-start, reply-start and trailing-silence timings are configured?";
    internal const string Answer = "The confirmed capture timing preferences are supplied in exact seconds; they do not prove active capture or authorize changes.";

    [Theory]
    [InlineData(RegisteredSetting.CommandStartWait, false)]
    [InlineData(RegisteredSetting.CommandStartWait, true)]
    [InlineData(RegisteredSetting.ReplyStartWait, false)]
    [InlineData(RegisteredSetting.ReplyStartWait, true)]
    [InlineData(RegisteredSetting.TrailingSilence, false)]
    [InlineData(RegisteredSetting.TrailingSilence, true)]
    public async Task OriginalVoiceReviewGetAndRetainedAnswerUseExactConfirmedTimingWithoutEffects(RegisteredSetting setting, bool saved)
    {
        var id = RegisteredSettingRegistry.Get(setting).Id;
        var transport = new SettingsDiscoveryWorkflowTests.Transport(
            $$$"""{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"settings.get","input":{"id":"{{{id}}}"}}]}""",
            JsonSerializer.Serialize(new { kind = "answer", answer = Answer }));
        await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport,
            savedTiming: saved ? new(7.1234567m, 20.7654321m, 0.5000001m) : null);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Desktop, HostOperation.Request);
        var session = await f.Workflow.ReviewAsync(Question, () => true, f.Token, ModelToolMode.SettingsDiscovery);
        session.Review.Question.Should().Be(Question);
        session.Review.Observations.Should().HaveCount(18);
        var observation = session.Review.Observations.Single(item => item.Setting == setting);
        observation.IsSaved.Should().Be(saved);
        observation.Revision.Should().Be(f.Timing.Get().Revision);
        session.Review.Text.Should().Contain(observation.Text).And.Contain("NEW Local context conversation");
        transport.Requests.Should().BeEmpty();
        f.Storage.Count("work_sessions").Should().Be(0);
        var result = await f.Workflow.AnswerAsync(session, () => true, f.Token);
        result.Outcome.Should().Be(SettingInspectionOutcome.Succeeded, "reason {0}", result.Receipt?.Message.Reason);
        transport.Requests.Should().HaveCount(2);
        transport.Requests.Should().OnlyContain(item => item.Context.UserRequest == Question
            && item.Context.Evidence.Single().Content == session.Review.Source
            && item.Provenance.Request.SessionId == session.Review.Destination.SessionId
            && item.Provenance.Request.Origin == RequestOrigin.ActivatedVoice);
        using var value = JsonDocument.Parse(((ModelToolResultMessage)transport.Requests[1].Messages[1]).Result.AsMemory());
        var actual = value.RootElement.GetProperty("settings").GetProperty("observation");
        actual.GetProperty("value").GetDecimal().Should().Be(observation.Value);
        actual.GetProperty("isSaved").GetBoolean().Should().Be(saved);
        actual.GetProperty("revision").GetInt64().Should().Be(observation.Revision);
        actual.GetProperty("text").GetString().Should().Be(observation.Text);
        var receipt = result.Receipt!;
        f.Storage.Reopen();
        await f.Storage.Store.InitializeAsync(f.Token);
        var retained = await f.Storage.Store.ReadConversationMessageAsync(receipt.Message.Request.SessionId,
            receipt.Message.Request.RequestId, f.Token);
        retained.Should().Be(receipt.Message);
        retained.Body.Should().Be(Question);
        retained.AssistantResponse!.Body.Should().Be(Answer);
        retained.Observations.Should().BeNull();
        (await f.Workflow.AnswerAsync(session, () => true, f.Token)).Outcome.Should().Be(SettingInspectionOutcome.Denied);
        AssertNoEffects(f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task TimingChangedBeforeDispatchGetOrFinalCannotPublishSuccessfulAnswer(int phase)
    {
        var transport = new SettingsDiscoveryWorkflowTests.Transport(
            """{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"settings.get","input":{"id":"voice.trailing-silence"}}]}""",
            JsonSerializer.Serialize(new { kind = "answer", answer = Answer }));
        await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Request);
        var session = await f.Workflow.ReviewAsync(Question, () => true, f.Token, ModelToolMode.SettingsDiscovery);
        void Change() => f.Preferences.Text["voice-capture-timing.txt"] = "1\n7\n20\n0.75";
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
        f.Controls.Audits.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded
            && item.ActionId == "setting.discovery.local");
        AssertNoEffects(f);
    }

    private static void AssertNoEffects(SettingInspectionWorkflowTests.Fixture f)
    {
        f.Host.IsQuiescent.Should().BeTrue();
        f.Preferences.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Storage.Count("scoped_grants").Should().Be(0);
        f.Storage.Count("host_questions").Should().Be(0);
    }
}
