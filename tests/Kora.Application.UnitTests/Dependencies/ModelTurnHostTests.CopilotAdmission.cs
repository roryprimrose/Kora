using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Losing_the_captured_hosted_profile_before_final_admission_cannot_authorize_egress(bool missing)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new HostedRuntime();
        var host = f.CopilotProfiles(runtime, reviews);
        var turn = (await host.AdmitCopilotAsync(f.Context(), Token)).Turn!;
        runtime.BeforeTransmission = () => turn.Profile = missing ? null : Profile(ProviderService.Local);
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
        runtime.BeforeTransmission = () => f.ProfileRead = () => new(f.Session with
        {
            IsActive = fault is not "inactive",
            SessionId = fault is "session" ? new(Guid.NewGuid()) : f.Session.SessionId,
            Generation = fault is "generation" ? new(2) : f.Session.Generation,
        }, f.SessionProfileRevision, f.SessionOverrides, fault is "held");
        var result = await host.RunAsync(turn, Token);
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Response.Should().BeNull();
        runtime.Transmissions.Should().Be(0);
    }

    [Theory]
    [InlineData("held")]
    [InlineData("inactive")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("revision")]
    public async Task HostedTransmissionRejectsProfileSnapshotDriftAfterContinuationRevalidation(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var runtime = new HostedRuntime();
        var host = f.CopilotProfiles(runtime, reviews);
        var turn = (await host.AdmitCopilotAsync(f.Context(), Token)).Turn!;
        var reads = 0;
        runtime.BeforeTransmission = () =>
        {
            f.ProfileRead = () =>
            {
                var current = new SessionProviderProfile(f.Session, f.SessionProfileRevision,
                    f.SessionOverrides, f.SessionProfileHeld);
                if (++reads != 2) { return current; }
                return current with
                {
                    Held = fault is "held",
                    Revision = fault is "revision" ? current.Revision + 1 : current.Revision,
                    Session = current.Session with
                    {
                        IsActive = fault is not "inactive",
                        SessionId = fault is "session" ? new(Guid.NewGuid()) : current.Session.SessionId,
                        Generation = fault is "generation" ? new(2) : current.Session.Generation,
                    },
                };
            };
        };

        var result = await host.RunAsync(turn, Token);

        result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        result.Response.Should().BeNull();
        reads.Should().BeGreaterThanOrEqualTo(2);
        runtime.Transmissions.Should().Be(0);
        f.ProfilePreferences.Writes.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        host.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("hosted")]
    [InlineData("uncomposed")]
    [InlineData("local-only")]
    [InlineData("local-first")]
    public async Task Ordinary_submit_uses_settings_selected_copilot_tool_free_and_never_substitutes_otherwise(string route)
    {
        using var f = new Fixture();
        var runtime = new HostedRuntime();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.CopilotProfiles(runtime, reviews);
        if (route is "local-only") { f.ProviderPreferences.Mode = ModelProviderMode.LocalOnly; }
        if (route is "local-first") { f.ProviderPreferences.Mode = ModelProviderMode.LocalFirst; }
        await using var service = f.ConversationService(route is "uncomposed" ? new TestAdmission(ModelTurnOutcome.Unavailable) : host,
            execution: host);
        (await service.ResolveRouteAsync(f.Session.SessionId, Token)).Should().Be(route switch
        {
            "hosted" => ConversationProviderRoute.Copilot,
            "local-first" => ConversationProviderRoute.LocalFirst,
            _ => ConversationProviderRoute.Local,
        });
        var receipt = await service.SubmitAsync(new(f.Session, null), "original explicit request", static () => true, Token);
        receipt.Message.Body.Should().Be("original explicit request");
        receipt.Message.AssistantResponse?.Body.Should().Be(route is "hosted" ? "actual-boundary-answer" : null);
        if (route is "hosted")
        {
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            runtime.ToolMode.Should().Be(ModelToolMode.None);
            f.Audits.Last(item => string.Equals(item.ActionId, "model.copilot.transmission", StringComparison.Ordinal))
                .ReasonCode.Should().Be(HostedTransmissionReviewService.AutomaticReason);
        }
        else { receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded); }
        runtime.Calls.Should().Be(route is "hosted" ? 1 : 0);
        runtime.Transmissions.Should().Be(route is "hosted" ? 1 : 0);
    }

    [Theory]
    [InlineData("held")]
    [InlineData("inactive")]
    [InlineData("foreign-session")]
    [InlineData("unset")]
    [InlineData("local")]
    [InlineData("catalogue")]
    [InlineData("session-policy")]
    public async Task Settings_route_preview_falls_back_to_local_shape_without_a_current_hosted_profile(string fault)
    {
        using var f = new Fixture();
        var runtime = new HostedRuntime();
        var host = f.CopilotProfiles(runtime, new(f, f.Time));
        switch (fault)
        {
            case "held": f.SessionProfileHeld = true; break;
            case "inactive": f.Session = f.Session with { IsActive = false }; break;
            case "foreign-session":
                f.ProfileRead = () => new(f.Session with { SessionId = new(Guid.NewGuid()) }, f.SessionProfileRevision, f.SessionOverrides, false);
                break;
            case "unset": f.ProfilePreferences.Value = new(ProviderService.Unset, []); break;
            case "local": f.ProfilePreferences.Value = new(ProviderService.Local, [Profile(ProviderService.Local)]); break;
            case "catalogue": f.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable; break;
            default:
                using (var root = f.Root())
                { (await host.InitializePolicyAsync(Token)).Policy!.Mode.Should().Be(ModelProviderMode.HostedPreferred); }
                f.ProviderPreferences.Mode = ModelProviderMode.LocalOnly;
                break;
        }
        (await host.ResolveConversationRouteAsync(f.Session.SessionId, Token)).Should().Be(fault is "session-policy"
            ? ConversationProviderRoute.Copilot : ConversationProviderRoute.Local,
            "only the captured session policy, not a later preference change, selects the request shape");
        runtime.Calls.Should().Be(0);
        f.ProfilePreferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Copilot_text_stream_preserves_exact_request_and_final_only_retention_until_resources_join(bool cancel)
    {
        using var f = new Fixture();
        var runtime = new ConversationCopilotStream();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.CopilotProfiles(runtime, reviews);
        await using var service = f.ConversationService(host, execution: host);
        var updates = new List<Kora.Application.Hosting.ConversationAnswerPresentation>();
        service.ProvisionalAnswerChanged += (update, _) =>
        {
            f.Retained!.AssistantResponse.Should().BeNull();
            f.ExecutionCompletions.Should().Be(0);
            updates.Add(update);
            return ValueTask.CompletedTask;
        };
        var target = new Kora.Core.Storage.SessionWorkspaceEntry(f.Session, null);
        var pending = service.SubmitAsync(target, "original streamed request", () => true, Token);
        await runtime.Entered.Task.WaitAsync(Token);
        try
        {
            updates.Should().ContainSingle().Which.Update.Answer.Should().Be("safe preview");
            updates[0].IsCurrent().Should().BeTrue();
            if (cancel)
            {
                var running = (await service.ReadRunningConversationAsync(target, () => true, Token))!;
                await service.CancelRunningConversationAsync(running.Target, () => true, Token);
                updates[0].IsCurrent().Should().BeFalse();
            }
            pending.IsCompleted.Should().BeFalse();
            host.IsQuiescent.Should().BeFalse();
            f.ExecutionCompletions.Should().Be(0);
        }
        finally { runtime.Release.TrySetResult(); }
        var receipt = await pending;
        receipt.Message.Body.Should().Be("original streamed request");
        receipt.Message.Request.Should().Be(updates[0].Target.Request);
        receipt.Message.AssistantResponse?.Body.Should().Be(cancel ? null : "safe preview complete");
        receipt.Message.Outcome.Should().Be(cancel ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Succeeded);
        updates[0].IsCurrent().Should().BeFalse();
        f.ExecutionCompletions.Should().Be(1);
        host.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Copilot_text_stream_preserves_buffered_runtime_compatibility_without_previews()
    {
        using var f = new Fixture();
        var runtime = new HostedRuntime();
        var reviews = new HostedTransmissionReviewService(f, f.Time);
        var host = f.CopilotProfiles(runtime, reviews);
        await using var service = f.ConversationService(host, execution: host);
        var updates = 0;
        service.ProvisionalAnswerChanged += (_, _) => { updates++; return ValueTask.CompletedTask; };
        var receipt = await service.SubmitAsync(new(f.Session, null), "original", () => true, Token);
        receipt.Message.AssistantResponse!.Body.Should().Be("actual-boundary-answer");
        runtime.Calls.Should().Be(1);
        updates.Should().Be(0);
    }

    [Fact]
    public async Task OrdinaryCopilotRechecksCurrentAccountProfileAfterProviderCompletionBeforeFinalCommit()
    {
        using var f = new Fixture();
        var runtime = new ConversationCopilotStream();
        var host = f.CopilotProfiles(runtime, new(f, f.Time));
        await using var service = f.ConversationService(host, execution: host);
        service.RunningConversationChanged += status =>
        {
            if (status.Phase == Kora.Core.Conversation.ConversationExecutionPhase.Completing)
            { f.ProfileCatalogue.Status = ProviderCatalogueStatus.Unavailable; }
        };
        var pending = service.SubmitAsync(new(f.Session, null), "original", () => true, Token);
        await runtime.Entered.Task.WaitAsync(Token);
        runtime.Release.TrySetResult();
        var receipt = await pending;
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        receipt.Message.AssistantResponse.Should().BeNull();
        f.ExecutionCompletions.Should().Be(1);
    }

    private sealed class ConversationCopilotStream : ICopilotAnswerTurnRuntime
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsQuiescent => !Entered.Task.IsCompleted || Release.Task.IsCompleted;
        public Task<CopilotTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<Kora.Core.Tools.CapabilityReply>> invokeTool,
            Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admitTransmission, CancellationToken token) =>
            throw new InvalidOperationException("Text stream must use the SDK streaming seam.");
        public async Task<CopilotTurnReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<Kora.Core.Tools.CapabilityReply>> invokeTool,
            Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admitTransmission,
            Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken token)
        {
            ModelToolProtocol.ReadContext(context.Span, provenance).ToolMode.Should().Be(ModelToolMode.None);
            var final = new CopilotTransmission(provenance, profile, new("https://api.githubcopilot.com/chat/completions"),
                System.Text.Encoding.UTF8.GetBytes("{\"model\":\"exact-model\",\"messages\":[\"original\"]}"));
            if (!await admitTransmission(final, token)) { return new(ModelTurnOutcome.DeniedEgress); }
            await publish(new(provenance, "safe preview"), token);
            Entered.TrySetResult();
            await Release.Task;
            return new(ModelTurnOutcome.Succeeded, "safe preview complete");
        }
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
