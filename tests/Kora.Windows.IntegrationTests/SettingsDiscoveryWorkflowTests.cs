using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Auditing;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class SettingsDiscoveryWorkflowTests
{
    private const string List = """{"kind":"toolCalls","calls":[{"callId":"list_1","capabilityId":"settings.list","input":{"count":3}}]}""";
    private const string Get = """{"kind":"toolCalls","calls":[{"callId":"get_2","capabilityId":"settings.get","input":{"id":"speech.summary-words"}}]}""";
    private const string Final = """{"kind":"answer","answer":"The confirmed default summary word cap is 80 words. A recommendation changes nothing."}""";

    [Theory]
    [InlineData("appearance.presence-timeout", 10, "seconds", RequestOrigin.LocalUi)]
    [InlineData("appearance.presence-size", 360, "pixels", RequestOrigin.ActivatedVoice)]
    [InlineData("appearance.dot-size", 100, "percent", RequestOrigin.LocalUi)]
    [InlineData("appearance.dot-density", 100, "percent", RequestOrigin.ActivatedVoice)]
    [InlineData("appearance.movement-speed", 100, "percent", RequestOrigin.LocalUi)]
    [InlineData("appearance.speech-scaling", 1, "boolean", RequestOrigin.ActivatedVoice)]
    [InlineData("appearance.speech-scale-amount", 100, "percent", RequestOrigin.LocalUi)]
    [InlineData("appearance.reduced-motion", 0, "boolean", RequestOrigin.ActivatedVoice)]
    public async Task PresenceQuestionReviewsAllValuesThenSeparatelyConfirmsExactDiscoveryAndRetainedAnswer(
        string id, int expected, string units, RequestOrigin origin)
    {
        const string answer = "The confirmed default inactivity timeout is 10 seconds, size is 360 pixels and movement speed is "
            + "100 percent. Reduced motion is false: follow Windows, not force motion. Preferences do not prove rendered behavior.";
        var get = $$$"""{"kind":"toolCalls","calls":[{"callId":"get_2","capabilityId":"settings.get","input":{"id":"{{{id}}}"}}]}""";
        var transport = new Transport(List, get, JsonSerializer.Serialize(new { kind = "answer", answer }));
        await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
        var session = await f.Workflow.ReviewAsync(
            "Why does Kora disappear so quickly, and what presence size and movement settings am I using?",
            () => true, f.Token, ModelToolMode.SettingsDiscovery);
        session.Review.Observations.Should().HaveCount(15).And.OnlyHaveUniqueItems(item => item.Setting);
        var observed = session.Review.Observations.Single(item =>
            string.Equals(RegisteredSettingRegistry.Get(item.Setting).Id, id, StringComparison.Ordinal));
        observed.Value.Should().Be(expected);
        observed.IsSaved.Should().BeFalse();
        session.Review.Text.Should().Contain(observed.Text).And.Contain(session.Review.Question)
            .And.Contain("NEW Local context conversation").And.Contain("No setting write").And.Contain("two minutes");
        transport.Requests.Should().BeEmpty();
        f.Storage.Count("work_sessions").Should().Be(0);
        var result = await f.Workflow.AnswerAsync(session, () => true, f.Token);
        result.Outcome.Should().Be(SettingInspectionOutcome.Succeeded, "reason {0}, request sizes {1}",
            result.Receipt?.Message.Reason, string.Join(", ", transport.Requests.Select(item => ModelToolProtocol.SerializeRequest(item).Length)));
        transport.Requests.Should().HaveCount(3);
        transport.Requests.Sum(item => ModelToolProtocol.SerializeRequest(item).Length)
            .Should().BeLessThanOrEqualTo(ModelToolProtocol.MaximumInputUtf8Bytes);
        transport.Requests.Should().OnlyContain(item => item.Context.UserRequest == session.Review.Question
            && item.Context.Evidence.Single().Content == session.Review.Source
            && item.Provenance.Request.SessionId == session.Review.Destination.SessionId
            && item.Provenance.Request.Origin == origin);
        using var value = JsonDocument.Parse(((ModelToolResultMessage)transport.Requests[2].Messages[3]).Result.AsMemory());
        var settings = value.RootElement.GetProperty("settings");
        settings.GetProperty("totalRecords").GetInt32().Should().Be(1);
        settings.TryGetProperty("nextOffset", out _).Should().BeFalse();
        var observation = settings.GetProperty("observation");
        observation.GetProperty("value").GetInt32().Should().Be(expected);
        observation.GetProperty("isSaved").GetBoolean().Should().BeFalse();
        observation.GetProperty("revision").GetInt64().Should().Be(observed.Revision);
        observation.GetProperty("text").GetString().Should().Be(observed.Text);
        settings.GetProperty("records")[0].GetProperty("descriptor").GetProperty("units").GetString().Should().Be(units);
        var receipt = result.Receipt!;
        f.Storage.Reopen();
        await f.Storage.Store.InitializeAsync(f.Token);
        var retained = await f.Storage.Store.ReadConversationMessageAsync(receipt.Message.Request.SessionId,
            receipt.Message.Request.RequestId, f.Token);
        retained.Should().Be(receipt.Message);
        retained.Body.Should().Be(session.Review.Question);
        retained.AssistantResponse!.Body.Should().Be(answer);
        retained.Observations.Should().BeNull();
        AssertNoEffects(f);
        f.Host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ReviewedLocalDiscoveryRetainsGenuineAnswerWithExactOriginalSourceAndNoEffects(RequestOrigin origin)
    {
        var transport = new Transport(List, Get, Final);
        await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
        var session = await f.Workflow.ReviewAsync("Which settings control spoken summaries?", () => true, f.Token,
            ModelToolMode.SettingsDiscovery);
        transport.Requests.Should().BeEmpty();
        var result = await f.Workflow.AnswerAsync(session, () => true, f.Token);
        result.Outcome.Should().Be(SettingInspectionOutcome.Succeeded, "reason {0}, request sizes {1}",
            result.Receipt?.Message.Reason, string.Join(", ", transport.Requests.Select(item => ModelToolProtocol.SerializeRequest(item).Length)));
        transport.Requests.Should().HaveCount(3);
        transport.Requests.Should().OnlyContain(item => item.Provenance.Request.SessionId == session.Review.Destination.SessionId
            && item.Provenance.Request.Origin == origin && item.Context.ToolMode == ModelToolMode.SettingsDiscovery
            && item.Context.UserRequest == session.Review.Question && item.Context.Evidence.Single().Content == session.Review.Source);
        transport.Requests[0].Context.Tools.Select(item => item.Id).Should().Equal(SettingsDiscoveryTools.List, SettingsDiscoveryTools.Get);
        using var metadata = JsonDocument.Parse(((ModelToolResultMessage)transport.Requests[1].Messages[1]).Result.AsMemory());
        metadata.RootElement.GetProperty("settings").GetProperty("records")[0].GetProperty("descriptor").GetProperty("id")
            .GetString().Should().Be("appearance.theme");
        using var value = JsonDocument.Parse(((ModelToolResultMessage)transport.Requests[2].Messages[3]).Result.AsMemory());
        value.RootElement.GetProperty("settings").GetProperty("observation").GetProperty("text").GetString()
            .Should().Be("speech.summary-words = 80 words (confirmed absent; live default)");
        var receipt = result.Receipt!;
        f.Storage.Reopen();
        await f.Storage.Store.InitializeAsync(f.Token);
        var retained = await f.Storage.Store.ReadConversationMessageAsync(receipt.Message.Request.SessionId,
            receipt.Message.Request.RequestId, f.Token);
        retained.Should().Be(receipt.Message);
        retained.AssistantResponse!.Body.Should().Be("The confirmed default summary word cap is 80 words. A recommendation changes nothing.");
        retained.Body.Should().Be(session.Review.Question);
        retained.Observations.Should().BeNull();
        AssertNoEffects(f);
        f.Host.IsQuiescent.Should().BeTrue();
        f.Controls.Audits.Where(item => string.Equals(item.ActionId, "setting.discovery.local", StringComparison.Ordinal))
            .Select(item => item.Outcome)
            .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        string.Join(' ', f.Controls.Audits.Select(item => item.ActionId + item.TargetId + item.ReasonCode))
            .Should().NotContain(session.Review.Question).And.NotContain(session.Review.Source);
        (await f.Workflow.AnswerAsync(session, () => true, f.Token)).Outcome.Should().Be(SettingInspectionOutcome.Denied);
        transport.Requests.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("unknown", "settings.get", """{"id":"credentials.token"}""")]
    [InlineData("path", "settings.get", """{"id":"C:\\private\\config"}""")]
    [InlineData("scope", "settings.get", """{"id":"speech.summary-words","scope":"session"}""")]
    [InlineData("duplicate", "settings.get", """{"id":"speech.summary-words","id":"speech.summary-words"}""")]
    [InlineData("unavailable", "settings.get", """{"id":"appearance.response-timeout"}""")]
    [InlineData("writes", "settings.reset", "{}")]
    [InlineData("other-family", "application.get_version", "{}")]
    public async Task InvalidOrUnsupportedCallsDoNotReadNewSourcesWriteOrRetainSuccessfulAnswers(string defect, string id, string input)
    {
        var transport = new Transport(
            $$"""{"kind":"toolCalls","calls":[{"callId":"bad_1","capabilityId":"{{id}}","input":{{input}}}]}""", Final);
        await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var session = await f.Workflow.ReviewAsync("What is confirmed?", () => true, f.Token, ModelToolMode.SettingsDiscovery);
        var result = await f.Workflow.AnswerAsync(session, () => true, f.Token);
        result.Outcome.Should().NotBe(SettingInspectionOutcome.Succeeded, defect);
        if (defect is "unavailable") { result.Outcome.Should().Be(SettingInspectionOutcome.Unavailable); }
        result.Receipt?.Message.AssistantResponse.Should().BeNull();
        transport.Requests.Should().ContainSingle();
        f.Controls.Audits.Should().NotContain(item => item.ActionId == "setting.discovery.local" && item.Outcome == SecurityAuditOutcome.Succeeded);
        AssertNoEffects(f);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("late-source")]
    [InlineData("permission")]
    [InlineData("session")]
    [InlineData("expiry")]
    public async Task CurrentnessAndPermissionChangesAcrossTransportAwaitSuppressStaleFinalAnswers(string change)
    {
        var transport = new Transport(List, Get, Final);
        await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var session = await f.Workflow.ReviewAsync("What is confirmed?", () => true, f.Token, ModelToolMode.SettingsDiscovery);
        transport.AfterRequest = async request =>
        {
            if (request.Round != (change is "late-source" ? 3 : 1)) { return; }
            switch (change)
            {
                case "source":
                case "late-source": f.Preferences.Text["presence-movement-speed-percent.txt"] = "150"; break;
                case "permission": f.Controls.CanControl = false; break;
                case "expiry": f.Storage.Time.Now = session.Review.ExpiresAt; break;
                case "session":
                    f.Storage.Mutate("UPDATE work_sessions SET generation = generation + 1;");
                    break;
            }
            await Task.CompletedTask;
        };
        var result = await f.Workflow.AnswerAsync(session, () => true, f.Token);
        result.Outcome.Should().NotBe(SettingInspectionOutcome.Succeeded);
        result.Receipt?.Message.AssistantResponse.Should().BeNull();
        f.Host.IsQuiescent.Should().BeTrue();
        AssertNoEffects(f);
    }

    [Fact]
    public async Task CancellationKeepsOwnershipUntilNoncooperativeTransportActuallyJoins()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Transport(List, Final)
        {
            AfterRequest = async _ => { entered.TrySetResult(); await release.Task; },
        };
        await using var f = new SettingInspectionWorkflowTests.Fixture(transport: transport);
        await f.InitializeAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var session = await f.Workflow.ReviewAsync("What is confirmed?", () => true, f.Token, ModelToolMode.SettingsDiscovery);
        using var cancellation = new CancellationTokenSource();
        var work = f.Workflow.AnswerAsync(session, () => true, cancellation.Token);
        await entered.Task.WaitAsync(f.Token);
        cancellation.Cancel();
        work.IsCompleted.Should().BeFalse();
        f.Host.IsQuiescent.Should().BeFalse();
        release.SetResult();
        var result = await work;
        result.Outcome.Should().NotBe(SettingInspectionOutcome.Succeeded);
        result.Receipt?.Message.AssistantResponse.Should().BeNull();
        f.Host.IsQuiescent.Should().BeTrue();
        transport.Requests.Should().ContainSingle();
        AssertNoEffects(f);
    }

    private static void AssertNoEffects(SettingInspectionWorkflowTests.Fixture f)
    {
        f.Preferences.Writes.Should().Be(0);
        f.Playback.Effects.Should().Be(0);
        f.Storage.Count("scoped_grants").Should().Be(0);
        f.Storage.Count("host_questions").Should().Be(0);
    }

    internal sealed class Transport(params string[] replies) : IModelToolTransport
    {
        internal List<ModelToolRequest> Requests { get; } = [];
        internal Func<ModelToolRequest, Task>? AfterRequest { get; set; }
        public async Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (AfterRequest is not null) { await AfterRequest(request); }
            var wire = Encoding.UTF8.GetBytes(replies[Requests.Count - 1]);
            var message = ModelToolProtocol.ParseAssistant(wire);
            return new(ModelTurnOutcome.Succeeded, request.Provenance, message,
                new(ModelToolProtocol.SerializeRequest(request).Length,
                    Math.Max(wire.Length, ModelToolProtocol.SerializeMessage(message).Length)));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
