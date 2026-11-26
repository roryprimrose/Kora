using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class ClaudeMessagesRuntimeTests
{
    [Theory]
    [InlineData(CustomizationKind.Skill, 2)]
    [InlineData(CustomizationKind.Agent, 2)]
    [InlineData(CustomizationKind.Skill, 0)]
    [InlineData(CustomizationKind.Agent, 1)]
    public async Task SelectedCopyUsesTwoExactUserMessagesAndEachPhysicalPostNeedsIndependentConsent(CustomizationKind kind, int approvals)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var context = new ModelContextEnvelope(root.Request, "Unchanged SystemPolicy", "  exact task\r\n ", [],
            f.Time.GetUtcNow(), f.Time.GetUtcNow().AddMinutes(1), ModelToolMode.None);
        var copy = new SelectedHostedDefinition(kind, "source", "revision", "reviewed", "Description",
            "---\nname: reviewed\n---\nComplete lower-trust source.");
        f.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? "{\"input_tokens\":25}" : Answer);
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        var offered = new List<ClaudeTransmission>();
        var reply = await runtime.RunSelectedAsync(new(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection),
            profile, context, copy, (review, token) =>
            {
                f.Requests.Count(request => request.Method is "POST").Should().Be(offered.Count);
                using var bytes = JsonDocument.Parse(review.Body.ToArray());
                var messages = bytes.RootElement.GetProperty("messages");
                messages.GetArrayLength().Should().Be(2);
                messages[0].GetProperty("role").GetString().Should().Be("user");
                messages[0].GetProperty("content")[0].GetProperty("text").GetString().Should().Be(copy.SourceText);
                messages[1].GetProperty("role").GetString().Should().Be("user");
                messages[1].GetProperty("content")[0].GetProperty("text").GetString().Should().Be(context.UserRequest);
                bytes.RootElement.GetProperty("system")[0].GetProperty("text").GetString().Should().Be(context.SystemPolicy);
                bytes.RootElement.TryGetProperty("tools", out _).Should().BeFalse();
                offered.Add(review);
                return ValueTask.FromResult(offered.Count <= approvals);
            }, _ => ValueTask.FromResult(true), Token);
        reply.Outcome.Should().Be(approvals == 2 ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        reply.Answer.Should().Be(approvals == 2 ? "genuine exact answer" : null);
        var posts = f.Requests.Where(request => request.Method is "POST").ToArray();
        posts.Length.Should().Be(approvals);
        for (var i = 0; i < posts.Length; i++) { posts[i].Body.Should().Equal(offered[i].Body); }
        if (approvals == 2)
        {
            using var count = JsonDocument.Parse(posts[0].Body);
            using var message = JsonDocument.Parse(posts[1].Body);
            foreach (var name in new[] { "model", "system", "messages", "thinking" })
            { count.RootElement.GetProperty(name).GetRawText().Should().Be(message.RootElement.GetProperty(name).GetRawText()); }
        }
        runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("task")]
    [InlineData("system")]
    [InlineData("role")]
    [InlineData("extra")]
    [InlineData("omitted")]
    public async Task ActualSerializedSelectedInputCannotBeSubstitutedBeforeCountOrDisclosure(string fault)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var context = new ModelContextEnvelope(root.Request, "policy", "original task", [],
            f.Time.GetUtcNow(), f.Time.GetUtcNow().AddMinutes(1), ModelToolMode.None);
        var copy = new SelectedHostedDefinition(CustomizationKind.Skill, "source", "revision", "reviewed", "Description", "Exact source");
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection);
        f.Respond = request => ClaudeAccountBrokerTests.Fixture.Json(request.RequestUri!.AbsolutePath.EndsWith("count_tokens", StringComparison.Ordinal)
            ? "{\"input_tokens\":25}" : Answer);
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        (await runtime.RunSelectedAsync(provenance, profile, context, copy,
            (_, _) => ValueTask.FromResult(true), _ => ValueTask.FromResult(true), Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        var actual = JsonNode.Parse(f.Requests.Last().Body)!;
        var messages = actual["messages"]!.AsArray();
        if (fault is "source") { messages[0]!["content"]![0]!["text"] = "Truncated source"; }
        if (fault is "task") { messages[1]!["content"]![0]!["text"] = "Substituted task"; }
        if (fault is "system") { actual["system"]![0]!["text"] = "Promoted source policy"; }
        if (fault is "role") { messages[0]!["role"] = "assistant"; }
        if (fault is "extra") { messages.Add(messages[1]!.DeepClone()); }
        if (fault is "omitted") { messages.RemoveAt(0); }
        f.Requests.Clear();
        var account = (await broker.ResolveAsync(profile, Token))!;
        using var physical = f.CreateHttp();
        var reviews = 0;
        using var handler = new ClaudeSdkRequestHandler(new(physical, broker), account, provenance, profile, 8192, 4096,
            (_, _) => { reviews++; return ValueTask.FromResult(true); }, _ => ValueTask.FromResult(true),
            () => throw new InvalidOperationException("No resource failure expected."), _ => { },
            selectedContext: context, selection: copy);
        using var http = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        { Content = new StringContent(actual.ToJsonString(), Encoding.UTF8, "application/json") };
        await Assert.ThrowsAsync<InvalidDataException>(() => http.SendAsync(request, Token));
        reviews.Should().Be(0);
        f.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("review")]
    [InlineData("between-posts")]
    [InlineData("budget")]
    public async Task SelectedCurrentnessAndCombinedPhysicalBudgetPreventLaterDisclosure(string fault)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var now = f.Time.GetUtcNow();
        var context = new ModelContextEnvelope(root.Request, "policy", "original", [], now, now.AddMinutes(1), ModelToolMode.None);
        var copy = new SelectedHostedDefinition(CustomizationKind.Agent, "source", "revision", "reviewed", "Description",
            fault is "budget" ? new string('x', 17000) : "Exact source");
        var live = true;
        f.Respond = request =>
        {
            if (fault is "between-posts") { live = false; }
            return ClaudeAccountBrokerTests.Fixture.Json("{\"input_tokens\":25}");
        };
        await using var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance, f.CreateHttp);
        var offers = 0;
        var reply = await runtime.RunSelectedAsync(new(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection),
            f.Profile(catalogue), context, copy, (_, _) =>
            {
                offers++;
                if (fault is "review") { live = false; }
                return ValueTask.FromResult(true);
            }, _ => ValueTask.FromResult(live), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        reply.Answer.Should().BeNull();
        offers.Should().Be(fault is "budget" ? 0 : 1);
        f.Requests.Count(request => request.Method is "POST").Should().Be(fault is "between-posts" ? 1 : 0);
    }
}
