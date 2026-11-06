using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Fact]
    public async Task SettingsDiscoveryUsesClosedNativeSchemasAndCanonicalInertResultsWithoutExtraTools()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        const string native = """{"model":"deterministic-fake:one","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"settings.get","arguments":{"id":"speech.summary-words"}}}]},"done":true}""";
        f.Chat = (message, _) => Task.FromResult(Json(message, f.Bodies.Count == 1 ? native : Final));
        var first = await f.Transport.CompleteAsync(f.Request(toolMode: ModelToolMode.SettingsDiscovery), Token);
        first.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var calls = first.Message.Should().BeOfType<ModelAssistantToolCalls>().Subject;
        using var wire = JsonDocument.Parse(f.Bodies[0]);
        var tools = wire.RootElement.GetProperty("tools");
        tools.GetArrayLength().Should().Be(2);
        var list = tools[0].GetProperty("function");
        list.GetProperty("name").GetString().Should().Be(SettingsDiscoveryTools.List);
        list.GetProperty("parameters").GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        list.GetProperty("parameters").GetProperty("properties").GetProperty("count").GetProperty("maximum")
            .GetInt32().Should().Be(6);
        var get = tools[1].GetProperty("function");
        get.GetProperty("name").GetString().Should().Be(SettingsDiscoveryTools.Get);
        get.GetProperty("parameters").GetProperty("required").EnumerateArray().Select(item => item.GetString())
            .Should().Equal("id");
        get.GetProperty("parameters").GetProperty("properties").GetProperty("id").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()).Should().Equal(RegisteredSettingRegistry.Settings.Select(item => item.Id));
        var result = ModelToolProtocol.CreateToolResult(calls.Calls.Single(), SettingsDiscoveryTools.Read(
            [new SettingObservation(RegisteredSetting.SummaryWords, 80, false, 1)],
            SettingsDiscoveryTools.Get, """{"id":"speech.summary-words"}"""));
        var second = await f.Transport.CompleteAsync(f.Request([calls, result], 2, toolMode: ModelToolMode.SettingsDiscovery), Token);
        second.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        using var sent = JsonDocument.Parse(f.Bodies[1]);
        sent.RootElement.GetProperty("messages")[3].GetProperty("content").GetString()
            .Should().Be(Encoding.UTF8.GetString(result.Result.AsSpan()));
        second.Message.Should().BeOfType<ModelFinalResponse>().Which.Answer.Should().Be("Genuine answer");
        f.Bodies.Should().HaveCount(2);
    }
}
