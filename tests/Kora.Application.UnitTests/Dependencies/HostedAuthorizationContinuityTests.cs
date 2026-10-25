using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

[Collection("Host tracing")]
public sealed class HostedAuthorizationContinuityTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public HostedAuthorizationContinuityTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("unused")]
    [InlineData("context")]
    [InlineData("issuer")]
    [InlineData("service")]
    [InlineData("adapter")]
    [InlineData("owner")]
    public void Single_hosted_invariant_requires_used_exact_context_service_and_adapter_ownership(string fault)
    {
        var f = new ClaudeModelTurnAdapterTests.Fixture();
        using var root = f.Root();
        var issued = f.Reviews.PrepareClaudeTurn(f.Context, f.Profile);
        issued.Used = fault is "unused" ? 0 : 1;
        var adapter = new ClaudeModelTurnAdapter(new GuardRuntime(), fault is "owner" ? new(f, f.Time) : f.Reviews);
        var registration = new ModelProviderRegistration(f.Provenance.Selection with
            { Provider = fault is "service" ? ModelProviderIdentity.Copilot : ModelProviderIdentity.Claude },
            ModelQualificationGate.None, f.Time.Now.AddMinutes(1), fault is "adapter" ? new GuardAdapter() : adapter);
        using var foreign = fault is "issuer" ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request) : null;
        var context = fault is "context" ? new(f.Request, "policy", "body", [], f.Time.Now, f.Time.Now.AddMinutes(1), ModelToolMode.None) : f.Context;
        ModelTurnHost.HostedAuthorized(registration, fault is "missing" ? null : issued, context, foreign ?? root)
            .Should().Be(fault is "valid");
        if (fault is "valid")
        {
            ModelTurnHost.BindHosted(adapter, issued, _ => ValueTask.FromResult(true)).Should().BeOfType<ClaudeModelTurnAdapter>();
            Assert.Throws<InvalidOperationException>(() => ModelTurnHost.BindHosted(new GuardAdapter(), issued,
                _ => ValueTask.FromResult(true)));
        }
    }

    [Theory]
    [InlineData("service")]
    [InlineData("tool-mode")]
    [InlineData("evidence")]
    [InlineData("issuer")]
    [InlineData("expired")]
    public void Claude_preparation_never_relabels_another_service_or_includes_tools_and_sources(string fault)
    {
        var f = new ClaudeModelTurnAdapterTests.Fixture();
        using var root = f.Root();
        var context = fault is "tool-mode" ? new(f.Request, "policy", "body", [], f.Time.Now, f.Time.Now.AddMinutes(1)) : f.Context;
        if (fault is "evidence") { context = new(f.Request, "policy", "body",
            [new(new(Guid.NewGuid()), f.Request, new(1), "not implicit context", ModelEvidenceDisclosure.HostedEligible)],
            f.Time.Now, f.Time.Now.AddMinutes(1), ModelToolMode.None); }
        if (fault is "issuer") { root.Complete(HostOperationOutcome.Completed); }
        if (fault is "expired") { f.Time.Now = context.ExpiresAt; }
        Assert.Throws<InvalidOperationException>(() => f.Reviews.PrepareClaudeTurn(context,
            fault is "service" ? f.Profile with { Service = ProviderService.Copilot } : f.Profile));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deny")]
    [InlineData("foreign-host")]
    [InlineData("revoked")]
    [InlineData("stale-current")]
    [InlineData("success")]
    public async Task Shared_final_review_retains_native_only_one_send_and_exact_authority(string fault)
    {
        var f = new ClaudeModelTurnAdapterTests.Fixture();
        using var root = f.Root();
        var authorization = f.Reviews.PrepareClaudeTurn(f.Context, f.Profile);
        var transmission = new ClaudeTransmission(f.Provenance, f.Profile, new("https://api.anthropic.com/v1/messages/count_tokens"),
            new byte[] { 1 }, [KeyValuePair.Create("Content-Type", "application/json")]);
        HostedTransmissionReview? observed = null;
        var live = true;
        f.Reviews.ClaudeRetired += review => review.IsPending.Should().BeFalse();
        if (fault is not "missing")
        {
            f.Reviews.ClaudeOffered += review =>
            {
                observed = review;
                review.Method.Should().Be("POST");
                review.Destination.Should().Be(transmission.Destination);
                review.Body.Should().Equal(transmission.Body);
                review.Headers.Should().Equal(transmission.Headers);
                review.Profile.Should().Be(f.Profile);
                if (fault is "foreign-host")
                {
                    using var foreign = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                        HostActivityLayer.Application, HostOperation.Request);
                    f.Reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeFalse();
                }
                f.Reviews.Confirm(review, true, RequestOrigin.HostSystem).Should().BeFalse();
                f.Reviews.Confirm(review, true, RequestOrigin.ActivatedVoice).Should().BeFalse();
                f.Reviews.Confirm(review, fault is not ("deny" or "foreign-host"), RequestOrigin.LocalUi).Should().BeTrue();
                if (fault is "revoked") { root.Complete(HostOperationOutcome.Failed); }
                if (fault is "stale-current") { live = false; }
            };
        }
        var approved = await f.Reviews.ReviewAsync(authorization, transmission, _ => ValueTask.FromResult(live), Token);
        approved.Should().Be(fault is "success");
        if (observed is not null)
        {
            observed.IsPending.Should().BeFalse();
            f.Reviews.Confirm(observed, true, RequestOrigin.LocalUi).Should().BeFalse();
        }
        f.Audits.Last().Outcome.Should().Be(fault is "success" ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Denied);
        HostedTurnAuthorization.MaximumDuration.Should().Be(CopilotTurnAuthorization.MaximumDuration);
    }

    private sealed class GuardRuntime : IClaudeTurnRuntime
    {
        public bool IsQuiescent => true;
        public Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile, ModelContextEnvelope context,
            Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit, Func<CancellationToken, ValueTask<bool>> current,
            CancellationToken token) => throw new InvalidOperationException("Invariant inspection must not enter a runtime.");
    }
    private sealed class GuardAdapter : IModelTurnAdapter
    {
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile, ReadOnlyMemory<byte> context,
            Func<string, string, CancellationToken, ValueTask<Kora.Core.Tools.CapabilityReply>> invokeTool, CancellationToken token) =>
            throw new InvalidOperationException("Foreign adapter must never be dispatched.");
    }
}
