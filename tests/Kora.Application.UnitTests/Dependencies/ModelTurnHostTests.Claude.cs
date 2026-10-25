using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("reviews")]
    [InlineData("configuration")]
    [InlineData("sessions")]
    [InlineData("ambient")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("request")]
    [InlineData("tools")]
    [InlineData("evidence")]
    [InlineData("held")]
    [InlineData("inactive")]
    [InlineData("session")]
    [InlineData("privacy")]
    [InlineData("unset")]
    [InlineData("local")]
    [InlineData("catalogue")]
    [InlineData("changed-observation")]
    public async Task Claude_admission_preserves_the_common_original_host_and_exact_service_boundaries(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var runtime = new ClaudeRuntime();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.ClaudeProfiles(runtime, reviews, fault is not "configuration", fault is not "sessions", fault is not "reviews");
        var context = f.Context(fault is "request" ? HostRequest.Create(RequestOrigin.LocalUi) : f.Request,
            toolMode: fault is "tools" ? ModelToolMode.ReadOnly : ModelToolMode.None);
        if (fault is "evidence") { context = new(f.Request, "policy", "body",
            [new(new(Guid.NewGuid()), f.Request, new(1), "not context", ModelEvidenceDisclosure.HostedEligible)],
            f.Time.Now, f.Time.Now.AddMinutes(1), ModelToolMode.None); }
        if (fault is "ambient") { Activity.Current = null; }
        if (fault is "stopped") { root.Activity!.Stop(); Activity.Current = root.Activity; }
        if (fault is "completed") { root.Complete(HostOperationOutcome.Completed); }
        if (fault is "held") { f.SessionProfileHeld = true; }
        if (fault is "inactive") { f.Session = f.Session with { IsActive = false }; }
        if (fault is "session") { f.Session = f.Session with { SessionId = new(Guid.NewGuid()) }; }
        if (fault is "privacy") { f.AfterProfileRead = () => f.CanControl = false; }
        if (fault is "unset") { f.ProfilePreferences.Value = new(ProviderService.Unset, []); }
        if (fault is "local") { f.ProfilePreferences.Value = new(ProviderService.Local, [Profile(ProviderService.Local)]); }
        if (fault is "catalogue") { f.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable; }
        if (fault is "changed-observation") { f.ProfileCatalogue.AfterObservation = () => f.CanControl = false; }
        try
        {
            if (fault is "changed-observation")
            { await Assert.ThrowsAsync<InvalidOperationException>(() => host.AdmitClaudeAsync(context, Token)); }
            else { (await host.AdmitClaudeAsync(context, Token)).Turn.Should().BeNull(); }
            runtime.Calls.Should().Be(0);
        }
        finally { Activity.Current = root.Activity; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Claude_canonical_retained_route_requires_exact_native_final_consent(bool approve)
    {
        using var f = new Fixture(RequestOrigin.ActivatedVoice);
        var runtime = new ClaudeRuntime();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.ClaudeProfiles(runtime, reviews);
        reviews.ClaudeOffered += review =>
        {
            using var presentation = review.Presentation();
            reviews.Confirm(review, approve, RequestOrigin.LocalUi).Should().BeTrue();
        };
        var receipt = await f.ConversationService(host, execution: host)
            .SubmitClaudeAsync(new(f.Session, null), "original Claude body", () => true, Token);
        receipt.Message.Body.Should().Be("original Claude body");
        receipt.Message.Outcome.Should().Be(approve ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.DeniedEgress);
        receipt.Message.AssistantResponse?.Body.Should().Be(approve ? "genuine Claude answer" : null);
        runtime.Context!.ToolMode.Should().Be(ModelToolMode.None);
        runtime.Context.Evidence.Should().BeEmpty();
        runtime.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Claude_uncomposed_route_and_ordinary_hosted_request_never_substitute_an_adapter()
    {
        using var f = new Fixture();
        var receipt = await f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable))
            .SubmitClaudeAsync(new(f.Session, null), "original", () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        using var ordinary = new Fixture();
        using var root = ordinary.Root();
        var runtime = new ClaudeRuntime();
        var host = ordinary.ClaudeProfiles(runtime, new(ordinary, ordinary.Time));
        (await host.AdmitAsync(ModelTurnChoice.Hosted, ordinary.Context(toolMode: ModelToolMode.None), null, Token))
            .Result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        runtime.Calls.Should().Be(0);
    }

    private sealed partial class Fixture
    {
        internal ModelTurnHost ClaudeProfiles(IClaudeTurnRuntime runtime, HostedTransmissionReviewService reviews,
            bool configuration = true, bool sessions = true, bool composeReviews = true)
        {
            Time.ExpectedDeadline = HostedTurnAuthorization.MaximumDuration;
            ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
            ProfilePreferences.Value = new(ProviderService.Claude, [Profile(ProviderService.Claude)]);
            ProviderPreferences.Mode = ModelProviderMode.HostedPreferred;
            var selection = ModelProviderSelection.OllamaCandidate with { Provider = ModelProviderIdentity.Claude };
            var host = new ModelTurnHost(this, this, this, registry, this, this, Time,
                [new(selection, Gates(selection), Time.Now.AddMinutes(5), new ClaudeModelTurnAdapter(runtime, reviews))],
                ProviderConfiguration, configuration ? ProfileConfiguration : null, sessions ? this : null,
                hostedReviews: composeReviews ? reviews : null);
            ownedHosts.Add(host);
            return host;
        }
    }

    private sealed class ClaudeRuntime : IClaudeTurnRuntime
    {
        public bool IsQuiescent => true;
        internal int Calls { get; private set; }
        internal ModelContextEnvelope? Context { get; private set; }
        public async Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
        {
            Calls++; Context = context;
            return await admit(new(provenance, profile, new("https://api.anthropic.com/v1/messages"), new byte[] { 123, 125 }, []), token)
                ? new(ModelTurnOutcome.Succeeded, "genuine Claude answer") : new(ModelTurnOutcome.DeniedEgress);
        }
    }
}
