using System.Diagnostics;
using System.Net;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class ClaudeFinalRequestHandlerTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ClaudeFinalRequestHandlerTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("changed-header")]
    [InlineData("ambient-header")]
    [InlineData("redirect")]
    [InlineData("oversize")]
    [InlineData("decline")]
    public async Task Exact_physical_boundary_rejects_changed_headers_redirects_and_unbounded_responses_without_retry(string fault)
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var accounts = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await accounts.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await accounts.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var account = (await accounts.ResolveAsync(profile, Token))!;
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection);
        var body = new byte[] { 123, 125 };
        var prepared = ClaudeFinalRequestHandler.Prepare(provenance, profile, account, "/v1/messages", body);
        body[0] = 0;
        if (fault is "changed-header")
        {
            prepared = new(provenance, profile, prepared.Destination, prepared.Body.ToArray(),
                prepared.Headers.SetItem("anthropic-version", "foreign-version"));
        }
        f.Respond = _ => fault is "redirect"
            ? new(HttpStatusCode.Found) { Headers = { Location = new("https://foreign.invalid/") } }
            : ClaudeAccountBrokerTests.Fixture.Json(new string('x', 262145));
        using var http = f.CreateHttp();
        if (fault is "ambient-header") { http.DefaultRequestHeaders.Add("X-Ambient", "not reviewed"); }
        var handler = new ClaudeFinalRequestHandler(http, accounts);
        var reviews = 0;
        var run = () => handler.SendAsync(prepared, account, (review, _) =>
        {
            reviews++;
            review.Body[0].Should().Be(123);
            review.Headers["Host"].Should().Be("api.anthropic.com");
            f.Requests.Count(request => request.Method is "POST").Should().Be(0);
            return ValueTask.FromResult(fault is not "decline");
        }, _ => ValueTask.FromResult(true), Token);
        if (fault is "decline") { (await run()).Should().BeNull(); }
        else if (fault is "redirect") { await Assert.ThrowsAsync<HttpRequestException>(run); }
        else { await Assert.ThrowsAsync<InvalidDataException>(run); }
        f.Requests.Count(request => request.Method is "POST").Should().Be(fault is "redirect" or "oversize" ? 1 : 0);
        reviews.Should().Be(fault is "changed-header" or "ambient-header" ? 0 : 1);
    }
}
