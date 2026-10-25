using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class ClaudeMessagesRuntimeTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ClaudeMessagesRuntimeTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0, 0, ModelTurnOutcome.DeniedEgress)]
    [InlineData(1, 1, ModelTurnOutcome.DeniedEgress)]
    [InlineData(2, 2, ModelTurnOutcome.Succeeded)]
    public async Task Every_content_post_requires_new_consent_and_exact_counted_input(int approvals, int sends, ModelTurnOutcome outcome)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var context = new ModelContextEnvelope(request, "trusted policy", "  unchanged\r\nbody  ", [],
            f.Time.GetUtcNow(), f.Time.GetUtcNow().AddMinutes(1), ModelToolMode.None);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, catalogue.Models[0].Selection);
        f.Respond = message => ClaudeAccountBrokerTests.Fixture.Json(message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? "{\"input_tokens\":25}" : Answer);
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        var reviews = new List<ClaudeTransmission>();
        var result = await runtime.RunAsync(provenance, profile, context, (review, _) =>
        {
            f.Requests.Count(request => request.Method is "POST").Should().Be(reviews.Count);
            review.Headers.Should().ContainKey("Host").And.NotContainKey("x-api-key");
            Encoding.UTF8.GetString(review.Body.AsSpan()).Should().NotContain(ClaudeAccountBrokerTests.Fixture.Credential);
            reviews.Add(review);
            return ValueTask.FromResult(reviews.Count <= approvals);
        }, _ => ValueTask.FromResult(true), Token);
        result.Outcome.Should().Be(outcome);
        result.Answer.Should().Be(outcome == ModelTurnOutcome.Succeeded ? "genuine exact answer" : null);
        var posts = f.Requests.Where(request => request.Method is "POST").ToArray();
        posts.Length.Should().Be(sends);
        if (sends == 2)
        {
            using var count = JsonDocument.Parse(posts[0].Body);
            using var message = JsonDocument.Parse(posts[1].Body);
            foreach (var name in new[] { "model", "system", "messages", "thinking" })
            { message.RootElement.GetProperty(name).GetRawText().Should().Be(count.RootElement.GetProperty(name).GetRawText()); }
            message.RootElement.GetProperty("messages")[0].GetProperty("content").GetString().Should().Be(context.UserRequest);
            message.RootElement.TryGetProperty("tools", out _).Should().BeFalse();
            posts[1].Body.Should().Equal(reviews[1].Body);
        }
        runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("count-limit")]
    [InlineData("changed-approval")]
    [InlineData("revoked-account")]
    [InlineData("privacy")]
    public async Task Count_consent_does_not_authorize_second_send_after_boundaries_change(string fault)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
        using (var setup = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request))
        { await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token); }
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Windows, HostOperation.Request);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var now = f.Time.GetUtcNow();
        var context = new ModelContextEnvelope(request, "policy", "original voice", [], now, now.AddMinutes(1), ModelToolMode.None);
        var live = true;
        f.Respond = _ => ClaudeAccountBrokerTests.Fixture.Json("{\"input_tokens\":9000}");
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        var result = await runtime.RunAsync(new(new(Guid.NewGuid()), request, catalogue.Models[0].Selection),
            profile, context, (_, _) =>
            {
                if (fault is "changed-approval") { f.Approved = false; }
                if (fault is "revoked-account") { broker.Revoke(); }
                if (fault is "privacy") { live = false; }
                return ValueTask.FromResult(true);
            }, _ => ValueTask.FromResult(live), Token);
        result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        result.Answer.Should().BeNull();
        f.Requests.Count(request => request.Method is "POST").Should().Be(fault is "count-limit" ? 1 : 0);
    }

    [Theory]
    [InlineData("tool_use")]
    [InlineData("server_tool_use")]
    [InlineData("thinking")]
    [InlineData("unknown")]
    public void Authority_bearing_or_unknown_response_blocks_never_become_answer(string type)
    {
        var invalid = Answer.Replace("\"type\":\"text\"", "\"type\":\"" + type + "\"", StringComparison.Ordinal);
        var f = new ClaudeAccountBrokerTests.Fixture();
        var profile = new Kora.Core.Providers.ProviderProfileTuple(Kora.Core.Providers.ProviderService.Claude,
            f.Lease.Account, new(1), new("exact-model"), new("disabled"), new("8192"));
        Assert.Throws<InvalidDataException>(() => ClaudeMessagesRuntime.ParseAnswer(Encoding.UTF8.GetBytes(invalid), profile, 4096, 8192));
    }

    internal const string Answer = "{\"id\":\"msg_test\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"exact-model\","
        + "\"content\":[{\"type\":\"text\",\"text\":\"genuine exact answer\"}],\"stop_reason\":\"end_turn\","
        + "\"stop_sequence\":null,\"usage\":{\"input_tokens\":25,\"output_tokens\":5}}";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_ignoring_physical_http_stays_owned_and_late_answer_is_never_published(bool revokeAccount)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var now = f.Time.GetUtcNow();
        var context = new ModelContextEnvelope(root.Request, "policy", "body", [], now, now.AddMinutes(1), ModelToolMode.None);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Send = (message, _) =>
        {
            if (message.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal))
            { return Task.FromResult(ClaudeAccountBrokerTests.Fixture.Json("{\"input_tokens\":25}")); }
            entered.TrySetResult(); return release.Task;
        };
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var run = runtime.RunAsync(new(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection), profile, context,
            (_, _) => ValueTask.FromResult(true), _ => ValueTask.FromResult(true), cancellation.Token);
        await entered.Task.WaitAsync(Token);
        if (revokeAccount) { broker.Revoke(); }
        else { await cancellation.CancelAsync(); }
        var disposal = runtime.DisposeAsync().AsTask();
        runtime.IsQuiescent.Should().BeFalse(); run.IsCompleted.Should().BeFalse(); disposal.IsCompleted.Should().BeFalse();
        release.SetResult(ClaudeAccountBrokerTests.Fixture.Json(Answer));
        var reply = await run;
        reply.Outcome.Should().Be(ModelTurnOutcome.Cancelled); reply.Answer.Should().BeNull();
        await disposal;
        runtime.IsQuiescent.Should().BeTrue();
        f.Requests.Count(request => request.Method is "POST").Should().Be(2);
    }
}
