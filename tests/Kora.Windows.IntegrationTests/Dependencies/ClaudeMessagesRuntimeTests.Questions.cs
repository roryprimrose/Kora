using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class ClaudeMessagesRuntimeTests
{
    [Fact]
    public async Task FailedQuestionTransportDisposalQuarantinesOwnerWithoutClaimingReleaseOrReuse()
    {
        var f = new ClaudeAccountBrokerTests.Fixture();
        await using var broker = f.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);
        await broker.SignInAsync(ClaudeAccountBrokerTests.Fixture.Credential, () => true, Token);
        var catalogue = await broker.ObserveAsync(Token);
        var profile = f.Profile(catalogue);
        var now = f.Time.GetUtcNow();
        var context = new ModelContextEnvelope(root.Request, "question policy", "original", [], now,
            now.AddMinutes(2), ModelToolMode.HostedClarification);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), root.Request, catalogue.Models[0].Selection);
        var runtime = new ClaudeMessagesRuntime(broker, f.Time, NullLogger<ClaudeMessagesRuntime>.Instance,
            () => new HttpClient(new FailedQuestionDisposal()));
        try
        {
            var transport = (await runtime.OpenToolTurnAsync(provenance, profile, context,
                (_, _) => ValueTask.FromResult(true), _ => ValueTask.FromResult(true), Token))!;
            runtime.IsQuiescent.Should().BeFalse();
            await Assert.ThrowsAsync<IOException>(() => transport.DisposeAsync().AsTask());
            runtime.IsQuiescent.Should().BeFalse();
            (await runtime.OpenToolTurnAsync(provenance, profile, context,
                (_, _) => ValueTask.FromResult(true), _ => ValueTask.FromResult(true), Token)).Should().BeNull();
            f.Requests.Should().OnlyContain(item => string.Equals(item.Method, "GET", StringComparison.Ordinal));
        }
        finally { await Assert.ThrowsAsync<IOException>(() => runtime.DisposeAsync().AsTask()); }
    }

    private sealed class FailedQuestionDisposal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This test never admits a content send.");
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            throw new IOException("Synthetic question transport disposal failure.");
        }
    }
}
