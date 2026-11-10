using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Auditing;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Windows.IntegrationTests.SettingInspection;

[Collection("Host tracing")]
public sealed class WindowsSpeechRateSettingInspectionTests
{
    internal const string Question = "Which Windows-native rate preference is saved, what configured rate is effective, and why does it not change Kokoro?";
    internal const string Answer = "The saved Windows-native desired rate is 5. Configured effective is unavailable for Kokoro; its synthesis is unchanged. This is not measured playback speed or permission to speak.";

    [Theory]
    [InlineData(false, RequestOrigin.LocalUi)]
    [InlineData(false, RequestOrigin.ActivatedVoice)]
    [InlineData(true, RequestOrigin.LocalUi)]
    [InlineData(true, RequestOrigin.ActivatedVoice)]
    public async Task CanonicalLocalAnswerRetainsOnlyOriginalAndFinalAndJoinsWithoutPreferenceOrAudioEffects(bool discovery, RequestOrigin origin)
    {
        var final = JsonSerializer.Serialize(new { kind = "answer", answer = Answer });
        var transport = new SettingsDiscoveryWorkflowTests.Transport(discovery
            ? ["""{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"settings.get","input":{"id":"speech.windows-rate"}}]}""", final]
            : [final]);
        await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport,
            speechSelection: new(SpeechProviderIds.Kokoro, "af_heart"), observeWindowsRate: true, savedWindowsRate: 5);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
        var review = await f.Workflow.ReviewAsync(Question, () => true, f.Token,
            discovery ? ModelToolMode.SettingsDiscovery : ModelToolMode.None, SettingInspectionProfile.WindowsRate);
        review.Review.Observations.Should().HaveCount(2);
        review.Review.Source.Should().Contain("desired 5").And.Contain("configured effective null")
            .And.NotContain("Private catalogue").And.NotContain("Recovery");
        transport.Requests.Should().BeEmpty();
        f.Storage.Count("work_sessions").Should().Be(0);
        var result = await f.Workflow.AnswerAsync(review, () => true, f.Token);
        result.Outcome.Should().Be(SettingInspectionOutcome.Succeeded, "reason {0}", result.Receipt?.Message.Reason);
        transport.Requests.Should().HaveCount(discovery ? 2 : 1);
        if (discovery)
        {
            var tool = (ModelToolResultMessage)transport.Requests[1].Messages[1];
            using var reply = JsonDocument.Parse(tool.Result.AsMemory());
            reply.RootElement.GetProperty("settings").GetProperty("profile").GetString().Should().Be("WindowsRate");
            reply.RootElement.GetProperty("settings").GetProperty("observation").GetProperty("windowsRate")
                .GetProperty("effective").ValueKind.Should().Be(JsonValueKind.Null);
        }
        f.Storage.Reopen();
        await f.Storage.Store.InitializeAsync(f.Token);
        var retained = await f.Storage.Store.ReadConversationMessageAsync(result.Receipt!.Message.Request.SessionId,
            result.Receipt.Message.Request.RequestId, f.Token);
        retained.Body.Should().Be(Question);
        retained.AssistantResponse!.Body.Should().Be(Answer);
        retained.Request.Origin.Should().Be(origin);
        retained.Observations.Should().BeNull();
        f.Storage.Count("conversation_messages").Should().Be(1);
        f.Storage.Count("scoped_grants").Should().Be(0);
        f.Storage.Count("host_questions").Should().Be(0);
        f.Preferences.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Host.IsQuiescent.Should().BeTrue();
        f.Controls.Audits.Where(item => string.Equals(item.ActionId, discovery ? "setting.discovery.local" : "setting.inspection.local", StringComparison.Ordinal))
            .Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("rate-before")]
    [InlineData("provider-before")]
    [InlineData("rate-final")]
    [InlineData("provider-final")]
    [InlineData("private-final")]
    public async Task SourceAndProviderRevalidatedBeforeInferenceAndAfterFinalAwait(string defect)
    {
        await using var f = new SettingInspectionWorkflowTests.Fixture(
            speechSelection: new(SpeechProviderIds.Windows, "voice"), observeWindowsRate: true, savedWindowsRate: 5);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var review = await f.Workflow.ReviewAsync(Question, () => true, f.Token, profile: SettingInspectionProfile.WindowsRate);
        void Change()
        {
            if (defect.StartsWith("rate", StringComparison.Ordinal)) { f.Preferences.Lines["speech-windows-rate.txt"] = ["1", "4"]; }
            else if (defect.StartsWith("provider", StringComparison.Ordinal)) { f.Preferences.Lines["speech-selection.txt"] = ["1", SpeechProviderIds.Kokoro, "voice"]; }
            else { f.Controls.CanInspect = false; }
        }
        if (defect.EndsWith("before", StringComparison.Ordinal)) { Change(); }
        else { f.Adapter.BeforeReturn = Change; }
        var result = await f.Workflow.AnswerAsync(review, () => true, f.Token);
        result.Outcome.Should().NotBe(SettingInspectionOutcome.Succeeded);
        f.Adapter.Calls.Should().Be(defect.EndsWith("before", StringComparison.Ordinal) ? 0 : 1);
        f.Host.IsQuiescent.Should().BeTrue();
        f.Preferences.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Controls.Audits.Where(item => string.Equals(item.ActionId, "setting.inspection.local", StringComparison.Ordinal))
            .Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task DiscoveryCannotReadAnUnreviewedDefaultProfileValue()
    {
        var transport = new SettingsDiscoveryWorkflowTests.Transport(
            """{"kind":"toolCalls","calls":[{"callId":"get_1","capabilityId":"settings.get","input":{"id":"appearance.response-timeout"}}]}""");
        await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport,
            speechSelection: new(SpeechProviderIds.Kokoro, "af_heart"), observeWindowsRate: true, savedWindowsRate: 5);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var review = await f.Workflow.ReviewAsync(Question, () => true, f.Token, ModelToolMode.SettingsDiscovery, SettingInspectionProfile.WindowsRate);
        var result = await f.Workflow.AnswerAsync(review, () => true, f.Token);
        result.Outcome.Should().NotBe(SettingInspectionOutcome.Succeeded);
        transport.Requests.Should().ContainSingle();
        f.Host.IsQuiescent.Should().BeTrue();
        f.Preferences.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
    }
}
