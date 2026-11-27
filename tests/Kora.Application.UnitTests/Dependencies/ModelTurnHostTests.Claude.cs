using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;

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
    [InlineData(ModelProviderMode.HostedPreferred)]
    [InlineData(ModelProviderMode.LocalOnly)]
    public async Task Claude_settings_route_answers_ordinary_submit_tool_free_only_when_hosted_is_selected(ModelProviderMode mode)
    {
        using var f = new Fixture();
        var runtime = new ClaudeRuntime();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.ClaudeProfiles(runtime, reviews);
        f.ProviderPreferences.Mode = mode;
        await using var service = f.ConversationService(host, execution: host);
        var hosted = mode == ModelProviderMode.HostedPreferred;
        (await service.ResolveRouteAsync(f.Session.SessionId, Token)).Should()
            .Be(hosted ? ConversationProviderRoute.Claude : ConversationProviderRoute.Local);
        var receipt = await service.SubmitAsync(new(f.Session, null), "original Claude body", () => true, Token);
        receipt.Message.Body.Should().Be("original Claude body");
        receipt.Message.AssistantResponse?.Body.Should().Be(hosted ? "genuine Claude answer" : null);
        if (hosted)
        {
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            receipt.Message.Execution!.Selection.Provider.Should().Be(ModelProviderIdentity.Claude);
            runtime.Context!.ToolMode.Should().Be(ModelToolMode.None);
            runtime.Context.Evidence.Should().BeEmpty();
            runtime.Sends.Should().Equal((Profile(ProviderService.Claude), RequestOrigin.LocalUi));
            f.Audits.Last(item => string.Equals(item.ActionId, "model.claude.transmission", StringComparison.Ordinal))
                .ReasonCode.Should().Be(HostedTransmissionReviewService.AutomaticReason);
        }
        else { receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded); }
        runtime.Calls.Should().Be(hosted ? 1 : 0);
    }

    [Fact]
    public async Task Claude_uncomposed_route_and_ordinary_hosted_request_never_substitute_an_adapter()
    {
        using var f = new Fixture();
        var receipt = await f.ConversationService(new TestAdmission(ModelTurnOutcome.Unavailable))
            .SubmitAsync(new(f.Session, null), "original", () => true, Token);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        using var ordinary = new Fixture();
        using var root = ordinary.Root();
        var runtime = new ClaudeRuntime();
        var host = ordinary.ClaudeProfiles(runtime, new(ordinary, ordinary.Time));
        (await host.AdmitAsync(ModelTurnChoice.Hosted, ordinary.Context(toolMode: ModelToolMode.None), null, Token))
            .Result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        runtime.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ExplicitClaudeObservationRouteReusesRealHostCallbackAndFreshContinuationAdmission()
    {
        using var f = new Fixture();
        var runtime = new ClaudeRuntime(observations: true);
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.ClaudeProfiles(runtime, reviews);
        f.Time.ExpectedDeadline = ModelToolProtocol.Deadline;
        var versions = 0;
        f.OnVersion = () => versions++;
        var receipt = await f.ConversationService(host, execution: host).SubmitClaudeObservationsAsync(
            new(f.Session, null), "original observation body", [ReadOnlyCapabilityCatalog.Version], () => true, Token);
        receipt.Message.Body.Should().Be("original observation body");
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        (receipt.Message.AssistantResponse?.Body).Should().Be("The observed version is 1.0.");
        versions.Should().Be(1);
        f.Audits.Where(item => string.Equals(item.ActionId, "model.claude.transmission", StringComparison.Ordinal)
                && item.Outcome != Kora.Core.Auditing.SecurityAuditOutcome.Requested)
            .Should().HaveCount(4).And.OnlyContain(item => item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
        runtime.Context!.Tools.Select(tool => tool.Id).Should().Equal(ReadOnlyCapabilityCatalog.Version);
        runtime.Context.Evidence.Should().BeEmpty();
        host.IsQuiescent.Should().BeTrue();
    }

    private sealed partial class Fixture
    {
        internal ModelTurnHost ClaudeProfiles(IClaudeTurnRuntime runtime, HostedTransmissionReviewService reviews,
            bool configuration = true, bool sessions = true, bool composeReviews = true,
            IModelProviderRegistrationSource? registrationSource = null)
        {
            Time.ExpectedDeadline = HostedTurnAuthorization.MaximumDuration;
            ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
            ProfilePreferences.Value = new(ProviderService.Claude, [Profile(ProviderService.Claude)]);
            ProviderPreferences.Mode = ModelProviderMode.HostedPreferred;
            var selection = ModelProviderSelection.OllamaCandidate with { Provider = ModelProviderIdentity.Claude };
            var host = new ModelTurnHost(this, this, this, registry, this, this, Time,
                [new(selection, Gates(selection), Time.Now.AddMinutes(5),
                    new ClaudeModelTurnAdapter(runtime, reviews, Time, NullLogger<BoundedModelTurnAdapter>.Instance))],
                ProviderConfiguration, configuration ? ProfileConfiguration : null, sessions ? this : null, registrationSource,
                hostedReviews: composeReviews ? reviews : null);
            ownedHosts.Add(host);
            return host;
        }
    }

    private sealed class ClaudeRuntime(bool observations = false) : IClaudeToolTurnRuntime, IClaudeHistoryInputPreparation
    {
        public bool IsQuiescent => true;
        internal int Calls { get; private set; }
        internal ModelContextEnvelope? Context { get; private set; }
        internal Action? BeforeAdmit { get; set; }
        internal Func<ModelContextEnvelope, int> HistoryInputBytes { get; set; } = context => context.Serialize().Length * 2;
        public Task<int> MeasureHistoryInputAsync(ModelProviderSelection selection, ProviderProfileTuple profile,
            ModelContextEnvelope context, CancellationToken token) => Task.FromResult(HistoryInputBytes(context));
        internal List<(ProviderProfileTuple Profile, RequestOrigin Origin)> Sends { get; } = [];
        public async Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
        {
            Calls++; Context = context;
            BeforeAdmit?.Invoke();
            if (!await admit(new(provenance, profile, new("https://api.anthropic.com/v1/messages"), new byte[] { 123, 125 }, []), token))
            { return new(ModelTurnOutcome.DeniedEgress); }
            Sends.Add((profile, provenance.Request.Origin));
            return new(ModelTurnOutcome.Succeeded, "genuine Claude answer");
        }

        public Task<IModelToolTransport?> OpenToolTurnAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
            Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
        {
            Calls++; Context = context;
            return Task.FromResult<IModelToolTransport?>(observations
                ? new ReviewedClaudeTransport(profile, admit, context.ToolMode == ModelToolMode.HostedClarification) : null);
        }
    }

    private sealed class ReviewedClaudeTransport(ProviderProfileTuple profile,
        Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit, bool question = false) : IModelToolTransport
    {
        private readonly ToolTransport inner = new(question
            ? """{"kind":"question","prompt":"Which format?","options":["Compact","Detailed"],"minimum":1,"maximum":1,"callId":"question_1"}"""
            : VersionCall, FinalAnswer);
        public async Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken)
        {
            foreach (var path in new[] { "count_tokens", "" })
            {
                if (!await admit(new(request.Provenance, profile, new("https://api.anthropic.com/v1/messages"
                    + (path.Length == 0 ? "" : "/" + path)), new byte[] { 123, 125 }, []), cancellationToken))
                { return new(ModelTurnOutcome.DeniedEgress, request.Provenance); }
            }
            return await inner.CompleteAsync(request, cancellationToken);
        }
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
