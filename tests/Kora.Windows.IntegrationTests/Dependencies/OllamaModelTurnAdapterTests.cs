using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

[Collection("Host tracing")]
public sealed class OllamaModelTurnAdapterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("unguarded")]
    [InlineData("missing-profile")]
    [InlineData("missing-context")]
    [InlineData("closed-host")]
    [InlineData("process-exited")]
    [InlineData("revoked")]
    public async Task MissingExactAuthorityCannotCreateATransportOrUseLegacyReasoner(string state)
    {
        using var f = new WindowsOllamaRuntimeResolverTests.Fixture();
        using var listener = Listen();
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi), f.Selection);
        using var root = state is "missing-context" ? null
            : HostActivity.BeginRoot(provenance.Request, HostActivityLayer.Application, HostOperation.Request);
        var adapter = new OllamaModelTurnAdapter(f.Resolver, f.Value!, f.Time, NullLoggerFactory.Instance,
            _ => throw new InvalidOperationException("No transport may be created."));
        if (state is "process-exited") { f.ProcessCurrent = false; }
        if (state is "revoked") { await f.Revoked.CancelAsync(); }
        var profile = state is "missing-profile" ? null : f.Profile;
        var wire = new ModelContextEnvelope(provenance.Request, "Host policy.", "Request.", [], f.Time.Now,
            f.Time.Now.AddSeconds(15)).Serialize();
        var result = state is "unguarded"
            ? await adapter.RunAsync(provenance, profile, wire, Invoke, Token)
            : await adapter.RunAsync(provenance, profile, wire, Invoke,
                _ => ValueTask.FromResult(state is "closed-host" ? ModelTurnReason.HostAdmissionClosed : ModelTurnReason.None), Token);
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Response.Should().BeNull();
    }

    [Fact]
    public async Task TransportConstructionFailureRevokesTheIssuedBindingAndSurfacesTheFailure()
    {
        using var f = new WindowsOllamaRuntimeResolverTests.Fixture();
        using var listener = Listen();
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), HostRequest.Create(RequestOrigin.LocalUi), f.Selection);
        using var root = HostActivity.BeginRoot(provenance.Request, HostActivityLayer.Application, HostOperation.Request);
        WindowsOllamaToolEndpointBinding? captured = null;
        var adapter = new OllamaModelTurnAdapter(f.Resolver, f.Value!, f.Time, NullLoggerFactory.Instance, binding =>
        {
            captured = binding;
            throw new IOException("Construction failed.");
        });
        var action = () => adapter.RunAsync(provenance, f.Profile, ReadOnlyMemory<byte>.Empty, Invoke,
            _ => ValueTask.FromResult(ModelTurnReason.None), Token);
        await action.Should().ThrowAsync<IOException>();
        captured.Should().NotBeNull();
        captured!.Revoked.IsCancellationRequested.Should().BeTrue();
        captured.IsAdmitted().Should().BeFalse();
    }

    [Fact]
    public async Task RegistrationFactoryCannotTurnMissingQualificationIntoBlanketGates()
    {
        using var f = new WindowsOllamaRuntimeResolverTests.Fixture();
        f.Value = null;
        var factory = new OllamaModelProviderFactory(f.Resolver, f.Time, NullLoggerFactory.Instance);
        (await factory.ResolveAsync(f.Selection, f.Profile, Token)).Should().BeNull();
        f.Observations.Should().Be(0);
    }

    private static ValueTask<CapabilityReply> Invoke(string id, string input, CancellationToken token) =>
        throw new InvalidOperationException("No tool is admitted by this test.");

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
