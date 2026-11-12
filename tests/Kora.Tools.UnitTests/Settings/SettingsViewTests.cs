using AwesomeAssertions;
using Kora.Core.Tools;
using Kora.Tools.Settings;

namespace Kora.Tools.UnitTests.Settings;

public sealed class SettingsViewTests
{
    [Fact]
    public async Task PresentationConfirmsOnlyAfterTheAdmittedHostCallbackCompletes()
    {
        var queries = new List<string>();
        var result = await SettingsView.ExecuteAsync("""{"query":"voice speed"}""", (query, _) =>
        {
            queries.Add(query);
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
        queries.Should().Equal("voice speed");
        result.Outcome.Should().Be(CapabilityOutcome.Succeeded);
        SettingsNavigationTool.Matches(result).Should().BeTrue();
        result.Settings.Should().BeNull();
        result.ProviderSettings.Should().BeNull();
    }

    [Fact]
    public async Task InvalidInputCancellationAndPresentationFailureNeverProduceSuccess()
    {
        var calls = 0;
        Task Show(string _, CancellationToken __) { calls++; return Task.CompletedTask; }
        (await SettingsView.ExecuteAsync("""{"query":""}""", Show, TestContext.Current.CancellationToken))
            .Outcome.Should().Be(CapabilityOutcome.Denied);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await FluentActions.Awaiting(async () => await SettingsView.ExecuteAsync("""{"query":"theme"}""", Show, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(0);
        await FluentActions.Awaiting(async () => await SettingsView.ExecuteAsync("""{"query":"theme"}""",
            (_, _) => throw new InvalidOperationException("native unavailable"), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        using var late = new CancellationTokenSource();
        await FluentActions.Awaiting(async () => await SettingsView.ExecuteAsync("""{"query":"theme"}""", (_, _) =>
        {
            late.Cancel();
            return Task.CompletedTask;
        }, late.Token)).Should().ThrowAsync<OperationCanceledException>();
    }
}
