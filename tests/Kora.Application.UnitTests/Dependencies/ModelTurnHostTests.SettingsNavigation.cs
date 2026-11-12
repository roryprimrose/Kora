using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private const string ViewSettingCall =
        """{"kind":"toolCalls","calls":[{"callId":"view_1","capabilityId":"settings.view","input":{"query":"speech.windows-rate"}}]}""";

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task ConversationalSettingsNavigationUsesTheOriginalHostAndReturnsNoSettingValues(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        using var root = f.Root();
        var transport = new ToolTransport(ViewSettingCall, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.LoopHost(loop);
        var queries = new List<string>();
        host.BindSettingsNavigation((query, admitted, token) =>
        {
            admitted().Should().BeTrue();
            token.IsCancellationRequested.Should().BeFalse();
            HostActivity.RequireCurrent().Request.Should().BeSameAs(f.Request);
            queries.Add(query);
            return Task.CompletedTask;
        });
        var admission = await host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            f.Context(toolMode: ModelToolMode.InteractiveConversation), Token);
        var result = admission.Turn is { } turn ? await host.RunAsync(turn, Token) : admission.Result;
        result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        queries.Should().Equal("speech.windows-rate");
        transport.Requests.Should().HaveCount(2);
        var receipt = (ModelToolResultMessage)transport.Requests[1].Messages[1];
        using var json = JsonDocument.Parse(receipt.Result.ToArray());
        json.RootElement.GetProperty("reason").GetString().Should().Be(SettingsNavigationTool.ResultReason);
        json.RootElement.TryGetProperty("settings", out _).Should().BeFalse();
        json.RootElement.TryGetProperty("providerSettings", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("system")]
    [InlineData("invocation")]
    [InlineData("revoked")]
    [InlineData("rebound")]
    [InlineData("host")]
    [InlineData("inspect")]
    [InlineData("revision")]
    [InlineData("expiry")]
    [InlineData("native-failure")]
    public async Task NavigationDoesNotBorrowAHostOrReviveRevokedPresentation(string loss)
    {
        using var f = new Fixture(loss is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (loss is "invocation")
        {
            var request = f.Request;
            f.Request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid()));
            f.Task = new(new(f.Request, new(1), HostTaskState.IntentRecorded), new(1), "host", true, null);
        }
        using var root = f.Root();
        var transport = new ToolTransport(ViewSettingCall, FinalAnswer);
        await using var loop = CreateLoop(f, transport);
        var host = f.LoopHost(loop);
        var calls = 0;
        if (loss is not "unbound")
        {
            host.BindSettingsNavigation((_, admitted, _) =>
            {
                if (loss is "revoked") { f.CanControl = false; }
                if (loss is "rebound") { host.BindSettingsNavigation(null); }
                if (loss is "host") { f.Current = false; }
                if (loss is "inspect") { f.Inspect = false; }
                if (loss is "revision") { f.ControlRevision++; }
                if (loss is "expiry") { f.Time.Now += TimeSpan.FromMinutes(1); }
                if (loss is "native-failure") { throw new InvalidOperationException("Native presenter unavailable."); }
                if (!admitted()) { throw new OperationCanceledException("Navigation retired."); }
                calls++;
                return Task.CompletedTask;
            });
        }
        var admission = await host.AdmitAsync(ModelProviderSelection.OllamaCandidate,
            f.Context(toolMode: ModelToolMode.InteractiveConversation), Token);
        var result = admission.Turn is { } turn ? await host.RunAsync(turn, Token) : admission.Result;
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Response.Should().BeNull();
        calls.Should().Be(0);
        transport.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(ModelToolMode.ReadOnly)]
    [InlineData(ModelToolMode.None)]
    public async Task ReadOnlyAndToolFreeRequestsNeverAcquireNativeSettingsNavigation(ModelToolMode mode)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var transport = new ToolTransport(ViewSettingCall);
        await using var loop = CreateLoop(f, transport);
        var host = f.LoopHost(loop);
        var calls = 0;
        host.BindSettingsNavigation((_, _, _) => { calls++; return Task.CompletedTask; });
        var turn = (await host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(toolMode: mode), Token)).Turn!;
        var result = await host.RunAsync(turn, Token);
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        calls.Should().Be(0);
    }
}
