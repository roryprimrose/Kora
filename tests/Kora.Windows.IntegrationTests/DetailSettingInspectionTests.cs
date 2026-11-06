using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Auditing;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class DetailSettingInspectionTests
{
    internal const string Question = "Which Details presentation mode is actually confirmed?";

    [Theory]
    [InlineData(null, RequestOrigin.LocalUi)]
    [InlineData(DetailPresentationMode.Offer, RequestOrigin.ActivatedVoice)]
    [InlineData(DetailPresentationMode.LinkOnly, RequestOrigin.LocalUi)]
    [InlineData(DetailPresentationMode.OpenAutomatically, RequestOrigin.ActivatedVoice)]
    public async Task ExactReviewedModeIsDiscoverableWithoutWritesAndOriginalAnswerReopens(DetailPresentationMode? saved, RequestOrigin origin)
    {
        var expected = saved ?? DetailPresentationPreference.Default;
        var answer = "The confirmed " + (saved.HasValue ? "saved" : "default") + " Details presentation mode is "
            + DetailPresentationPreference.Format(expected) + ". This preference does not prove a runtime effect.";
        var offset = RegisteredSettingRegistry.Settings.ToList().FindIndex(item => item.Setting == RegisteredSetting.DetailPresentation);
        var transport = new SettingsDiscoveryWorkflowTests.Transport(
            $$$"""{"kind":"toolCalls","calls":[{"callId":"list_1","capabilityId":"settings.list","input":{"offset":{{{offset}}},"count":1}},{"callId":"get_1","capabilityId":"settings.get","input":{"id":"responses.detail-presentation"}}]}""",
            JsonSerializer.Serialize(new { kind = "answer", answer }));
        await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport, detailMode: saved);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
        var session = await f.Workflow.ReviewAsync(Question, () => true, f.Token, ModelToolMode.SettingsDiscovery);
        session.Review.Observations.Should().HaveCount(20).And.OnlyHaveUniqueItems(item => item.Setting);
        var observation = session.Review.Observations.Single(item => item.Setting == RegisteredSetting.DetailPresentation);
        observation.Value.Should().Be((int)expected);
        observation.IsSaved.Should().Be(saved.HasValue);
        session.Review.Text.Should().Contain(observation.Text).And.Contain(Question).And.Contain("NEW Local context conversation");
        transport.Requests.Should().BeEmpty();
        f.Storage.Count("work_sessions").Should().Be(0);
        var result = await f.Workflow.AnswerAsync(session, () => true, f.Token);
        result.Outcome.Should().Be(SettingInspectionOutcome.Succeeded, "reason {0}", result.Receipt?.Message.Reason);
        transport.Requests.Should().HaveCount(2);
        transport.Requests.Should().OnlyContain(item => item.Context.UserRequest == Question
            && item.Context.Evidence.Single().Content == session.Review.Source
            && item.Provenance.Request.SessionId == session.Review.Destination.SessionId && item.Provenance.Request.Origin == origin);
        using var list = JsonDocument.Parse(((ModelToolResultMessage)transport.Requests[1].Messages[1]).Result.AsMemory());
        var record = list.RootElement.GetProperty("settings").GetProperty("records")[0];
        record.GetProperty("descriptor").GetProperty("id").GetString().Should().Be("responses.detail-presentation");
        record.GetProperty("confirmedValueAvailable").GetBoolean().Should().BeTrue();
        using var get = JsonDocument.Parse(((ModelToolResultMessage)transport.Requests[1].Messages[2]).Result.AsMemory());
        var value = get.RootElement.GetProperty("settings").GetProperty("observation");
        value.GetProperty("value").GetInt32().Should().Be((int)expected);
        value.GetProperty("isSaved").GetBoolean().Should().Be(saved.HasValue);
        value.GetProperty("revision").GetInt64().Should().Be(observation.Revision);
        value.GetProperty("text").GetString().Should().Be(observation.Text);
        var receipt = result.Receipt!;
        f.Storage.Reopen();
        await f.Storage.Store.InitializeAsync(f.Token);
        var retained = await f.Storage.Store.ReadConversationMessageAsync(receipt.Message.Request.SessionId,
            receipt.Message.Request.RequestId, f.Token);
        retained.Should().Be(receipt.Message);
        retained.Body.Should().Be(Question);
        retained.AssistantResponse!.Body.Should().Be(answer);
        retained.Observations.Should().BeNull();
        (await f.Workflow.AnswerAsync(session, () => true, f.Token)).Outcome.Should().Be(SettingInspectionOutcome.Denied);
        transport.Requests.Should().HaveCount(2);
        AssertNoEffects(f);
    }

    [Theory]
    [InlineData(0, "source")]
    [InlineData(1, "source")]
    [InlineData(2, "source")]
    [InlineData(1, "marker")]
    [InlineData(1, "corrupt")]
    [InlineData(1, "revision")]
    [InlineData(1, "privacy")]
    [InlineData(1, "call")]
    [InlineData(1, "input")]
    public async Task SourceOrOriginalAdmissionChangeCannotDiscloseLateGetOrAnswer(int phase, string boundary)
    {
        var transport = new SettingsDiscoveryWorkflowTests.Transport(
            """{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"settings.get","input":{"id":"responses.detail-presentation"}}]}""",
            """{"kind":"answer","answer":"A stale answer must not be published."}""");
        await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Desktop, HostOperation.Request);
        var eligible = true;
        var session = await f.Workflow.ReviewAsync(Question, () => eligible, f.Token, ModelToolMode.SettingsDiscovery);
        void Change()
        {
            switch (boundary)
            {
                case "marker": f.Preferences.Text["detail-presentation-unconfirmed.txt"] = "1"; break;
                case "corrupt": f.Preferences.Text["detail-presentation.txt"] = "invalid"; break;
                case "revision": f.Details.HoldUnavailable(); f.Details.Observe(); break;
                case "privacy": f.Controls.CanInspect = false; break;
                case "call": f.Controls.ControlRevision++; break;
                case "input": eligible = false; break;
                default: f.Preferences.Text["detail-presentation.txt"] = "OpenAutomatically"; break;
            }
        }
        if (phase == 0) { Change(); }
        transport.AfterRequest = request =>
        {
            if (request.Round == phase) { Change(); }
            return Task.CompletedTask;
        };
        var result = await f.Workflow.AnswerAsync(session, () => eligible, f.Token);
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
