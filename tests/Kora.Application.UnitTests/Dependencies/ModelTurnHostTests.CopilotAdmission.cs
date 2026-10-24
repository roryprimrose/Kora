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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Losing_the_captured_hosted_profile_during_review_cannot_authorize_egress(bool missing)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new HostedRuntime();
        var host = f.CopilotProfiles(runtime, reviews);
        var turn = (await host.AdmitCopilotAsync(f.Context(), Token)).Turn!;
        reviews.Offered += review =>
        {
            turn.Profile = missing ? null : Profile(ProviderService.Local);
            reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue();
        };
        (await host.RunAsync(turn, Token)).Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        runtime.Transmissions.Should().Be(0);
    }

    [Fact]
    public async Task A_late_runtime_tool_callback_cannot_reenter_a_finished_host_turn()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        Func<string, string, CancellationToken, ValueTask<Kora.Core.Tools.CapabilityReply>>? callback = null;
        var runtime = new ProbeCopilotRuntime
        {
            Run = (invoke, _) =>
            {
                callback = invoke;
                return Task.FromResult(new CopilotTurnReply(ModelTurnOutcome.DeniedEgress));
            },
        };
        var host = f.CopilotProfiles(runtime, reviews);
        var turn = (await host.AdmitCopilotAsync(f.Context(), Token)).Turn!;
        (await host.RunAsync(turn, Token)).Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        var late = await callback!(Kora.Core.Tools.ReadOnlyCapabilityCatalog.Version, "{}", Token);
        late.Outcome.Should().Be(Kora.Core.Tools.CapabilityOutcome.Denied);
        late.Reason.Should().Be("copilot-turn-not-current");
    }

    [Theory]
    [InlineData("held")]
    [InlineData("inactive")]
    [InlineData("session")]
    [InlineData("generation")]
    public async Task Separate_profile_store_cannot_confer_authority_from_an_inconsistent_session_snapshot(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new HostedRuntime();
        var host = f.CopilotProfiles(runtime, reviews);
        var turn = (await host.AdmitCopilotAsync(f.Context(), Token)).Turn!;
        reviews.Offered += review =>
        {
            f.ProfileRead = () => new(f.Session with
            {
                IsActive = fault is not "inactive",
                SessionId = fault is "session" ? new(Guid.NewGuid()) : f.Session.SessionId,
                Generation = fault is "generation" ? new(2) : f.Session.Generation,
            }, f.SessionProfileRevision, f.SessionOverrides, fault is "held");
            reviews.Confirm(review, true, RequestOrigin.LocalUi).Should().BeTrue();
        };
        var result = await host.RunAsync(turn, Token);
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Response.Should().BeNull();
        runtime.Transmissions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_conversation_route_preserves_original_request_and_denies_uncomposed_or_unreviewed_egress(bool composed)
    {
        using var f = new Fixture();
        var runtime = new HostedRuntime();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.CopilotProfiles(runtime, reviews);
        var service = f.ConversationService(composed ? host : new TestAdmission(ModelTurnOutcome.Unavailable), execution: host);
        var receipt = await service.SubmitCopilotAsync(new(f.Session, null), "original explicit request", static () => true, Token);
        receipt.Message.Body.Should().Be("original explicit request");
        receipt.Message.Outcome.Should().Be(composed ? ModelTurnOutcome.DeniedEgress : ModelTurnOutcome.Unavailable);
        receipt.Message.AssistantResponse.Should().BeNull();
        runtime.Calls.Should().Be(composed ? 1 : 0);
        runtime.Transmissions.Should().Be(0);
    }

    [Fact]
    public async Task Hosted_preparation_cannot_survive_profile_replacement_between_resolution_and_admission()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.CopilotProfiles(new HostedRuntime(), reviews);
        var authorization = reviews.PrepareTurn(f.Context(), Profile(ProviderService.Copilot));
        f.ProfileCatalogue.Account = "replacement";
        f.ProfilePreferences.Value = new(ProviderService.Copilot,
            [authorization.Profile with { Account = new("replacement") }]);
        var result = await host.AdmitCopilotAsync(authorization, Token);
        result.Result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        result.Turn.Should().BeNull();
    }

    [Fact]
    public async Task Ordinary_hosted_admission_cannot_use_a_dynamic_source_to_remove_egress_denial()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var source = new RegistrationSource { Resolve = (_, _, _) => throw new InvalidOperationException("Must not resolve without authority") };
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Copilot, [Profile(ProviderService.Copilot)]);
        f.ProviderPreferences.Mode = ModelProviderMode.HostedPreferred;
        var host = f.ConfiguredProfiles(registrationSource: source);
        (await host.AdmitAsync(ModelTurnChoice.Hosted, f.Context(), null, Token)).Result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        source.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("reviews")]
    [InlineData("configuration")]
    [InlineData("sessions")]
    [InlineData("ambient")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("request")]
    [InlineData("held")]
    [InlineData("inactive")]
    [InlineData("session")]
    [InlineData("privacy")]
    [InlineData("unset")]
    [InlineData("local")]
    [InlineData("catalogue")]
    [InlineData("changed-observation")]
    public async Task Explicit_hosted_admission_fails_closed_before_runtime_when_authority_is_missing(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var runtime = new HostedRuntime();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.CopilotProfiles(runtime, reviews, fault is not "configuration", fault is not "sessions", fault is not "reviews");
        var context = f.Context(fault is "request" ? HostRequest.Create(RequestOrigin.LocalUi) : f.Request);
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
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => host.AdmitCopilotAsync(context, Token));
                runtime.Calls.Should().Be(0);
                return;
            }
            var result = await host.AdmitCopilotAsync(context, Token);
            result.Turn.Should().BeNull();
            result.Result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
            runtime.Calls.Should().Be(0);
        }
        finally { Activity.Current = root.Activity; }
    }

    [Theory]
    [InlineData("configuration")]
    [InlineData("ambient")]
    [InlineData("foreign")]
    [InlineData("expired")]
    public async Task Authorization_cannot_be_reused_under_a_missing_or_changed_original_context(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.CopilotProfiles(new HostedRuntime(), reviews, profileConfiguration: fault is not "configuration");
        var authorization = reviews.PrepareTurn(f.Context(), Profile(ProviderService.Copilot));
        if (fault is "expired") { f.Time.Now = authorization.Context.ExpiresAt; }
        if (fault is "ambient") { Activity.Current = null; }
        try
        {
            if (fault is "foreign")
            {
                using var foreign = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
                (await host.AdmitCopilotAsync(authorization, Token)).Turn.Should().BeNull();
            }
            else { (await host.AdmitCopilotAsync(authorization, Token)).Turn.Should().BeNull(); }
        }
        finally { Activity.Current = root.Activity; }
    }

    [Fact]
    public async Task Unconfirmed_provider_quiescence_is_an_explicit_failure_not_an_observation_spin_or_disposal_success()
    {
        using var f = new Fixture();
        var runtime = new ProbeCopilotRuntime { IsQuiescent = false };
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.UnconfirmedCopilot(runtime, reviews);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.WaitForQuiescenceAsync(Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.DisposeAsync().AsTask());
        host.IsQuiescent.Should().BeFalse();
    }

    private sealed partial class Fixture
    {
        internal ModelTurnHost UnconfirmedCopilot(ICopilotTurnRuntime runtime, HostedTransmissionReviewService reviews) =>
            new(this, this, this, registry, this, this, Time,
                [new(ModelProviderSelection.CopilotCandidate, Gates(ModelProviderSelection.CopilotCandidate),
                    Time.Now.AddMinutes(1), new CopilotModelTurnAdapter(runtime, reviews))]);
    }
}
