using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("local", true)]
    [InlineData("copilot", false)]
    [InlineData("claims-production", false)]
    [InlineData("foreign-evaluation", false)]
    [InlineData("local-preview", false)]
    [InlineData("claude-preview", false)]
    [InlineData("hosted-gate", false)]
    [InlineData("unknown-gate", false)]
    [InlineData("missing-host", false)]
    [InlineData("expired", false)]
    [InlineData("revoked", false)]
    public void Ollama_evaluation_is_separate_from_release_qualification_and_requires_current_host_admission(
        string state, bool admitted)
    {
        using var f = new Fixture();
        var evidence = ModelQualificationGate.UserReviewedOllamaEvaluation | ModelQualificationGate.IntegratedHost;
        if (state is "claims-production") { evidence |= ModelQualificationGate.LocalCandidateSelection; }
        if (state is "foreign-evaluation") { evidence |= ModelQualificationGate.UserReviewedCopilotEvaluation; }
        if (state is "local-preview") { evidence |= ModelQualificationGate.UserReviewedLocalEvaluation; }
        if (state is "claude-preview") { evidence |= ModelQualificationGate.UserReviewedClaudeEvaluation; }
        if (state is "hosted-gate") { evidence |= ModelQualificationGate.AllPathLifecycle; }
        if (state is "unknown-gate") { evidence |= (ModelQualificationGate)512; }
        if (state is "missing-host") { evidence &= ~ModelQualificationGate.IntegratedHost; }
        var registration = new ModelProviderRegistration(
            state is "copilot" ? ModelProviderSelection.CopilotCandidate : ModelProviderSelection.OllamaCandidate,
            evidence, state is "expired" ? f.Time.Now : f.Time.Now.AddMinutes(1), f.Adapter, () => state is not "revoked");
        registration.IsQualified(f.Time.Now).Should().Be(admitted);
    }

    [Theory]
    [InlineData("current", true)]
    [InlineData("missing-account", false)]
    [InlineData("missing-send-review", false)]
    [InlineData("claims-production", false)]
    [InlineData("foreign-provider", false)]
    public void Claude_preview_preserves_account_and_final_send_admission_without_claiming_lifecycle_qualification(string state, bool admitted)
    {
        using var f = new Fixture();
        var evidence = ModelQualificationGate.UserReviewedClaudeEvaluation | ModelQualificationGate.ExecutionAccount
            | ModelQualificationGate.DotNetFinalRequest | ModelQualificationGate.IntegratedHost;
        if (state is "missing-account") { evidence &= ~ModelQualificationGate.ExecutionAccount; }
        if (state is "missing-send-review") { evidence &= ~ModelQualificationGate.DotNetFinalRequest; }
        if (state is "claims-production") { evidence |= ModelQualificationGate.AllPathLifecycle; }
        var selection = ModelProviderSelection.CopilotCandidate with { Provider = ModelProviderIdentity.Claude };
        if (state is "foreign-provider") { selection = ModelProviderSelection.CopilotCandidate; }
        var registration = new ModelProviderRegistration(selection, evidence, f.Time.Now.AddMinutes(1), f.Adapter);
        registration.IsQualified(f.Time.Now).Should().Be(admitted);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false, "admitted", ModelTurnOutcome.Succeeded)]
    [InlineData(false, "missing-host", ModelTurnOutcome.Unavailable)]
    [InlineData(false, "revoked", ModelTurnOutcome.Unavailable)]
    [InlineData(true, "admitted", ModelTurnOutcome.Succeeded)]
    [InlineData(true, "missing-host", ModelTurnOutcome.Unavailable)]
    [InlineData(true, "revoked", ModelTurnOutcome.Unavailable)]
    public async Task Local_evaluation_uses_the_actual_host_without_claiming_candidate_qualification(
        bool nativeEvaluation, string state, ModelTurnOutcome expected)
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var evidence = nativeEvaluation ? ModelQualificationGate.UserReviewedOllamaEvaluation
            : ModelQualificationGate.UserReviewedLocalEvaluation;
        if (state is not "missing-host") { evidence |= ModelQualificationGate.IntegratedHost; }
        var source = new RegistrationSource
        {
            Resolve = (selection, _, _) => Task.FromResult<ModelProviderRegistration?>(new(selection,
                evidence, f.Time.Now.AddMinutes(1), f.Adapter, () => state is not "revoked")),
        };
        var host = f.ConfiguredProfiles(registrationSource: source);
        var admitted = await host.AdmitAsync(ModelTurnChoice.Local, f.Context(), null, Token);
        admitted.Result.Outcome.Should().Be(expected);
        if (expected == ModelTurnOutcome.Succeeded)
        { (await host.RunAsync(admitted.Turn!, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded); }
        f.Adapter.Calls.Should().Be(expected == ModelTurnOutcome.Succeeded ? 1 : 0);
    }

    [Theory]
    [InlineData("foreign-provider")]
    [InlineData("claims-qualified")]
    [InlineData("copilot-evaluation")]
    public void Local_preview_cannot_claim_production_evidence_or_another_provider(string failure)
    {
        using var f = new Fixture();
        var flags = ModelQualificationGate.UserReviewedLocalEvaluation | ModelQualificationGate.IntegratedHost;
        if (failure is "claims-qualified") { flags |= ModelQualificationGate.LocalCandidateSelection; }
        if (failure is "copilot-evaluation") { flags |= ModelQualificationGate.UserReviewedCopilotEvaluation; }
        var selection = failure is "foreign-provider" ? ModelProviderSelection.CopilotCandidate : ModelProviderSelection.OllamaCandidate;
        var registration = new ModelProviderRegistration(selection, flags, f.Time.Now.AddMinutes(1), f.Adapter);
        registration.IsQualified(f.Time.Now).Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("copilot", true)]
    [InlineData("local", false)]
    [InlineData("missing-final-gate", false)]
    [InlineData("missing-account", false)]
    [InlineData("missing-host", false)]
    [InlineData("claims-production", false)]
    [InlineData("revoked", false)]
    public void Evaluation_is_Copilot_only_and_still_requires_final_request_account_and_host_admission(string state, bool admitted)
    {
        using var f = new Fixture();
        var evidence = ModelQualificationGate.UserReviewedCopilotEvaluation | ModelQualificationGate.DotNetFinalRequest
            | ModelQualificationGate.ExecutionAccount | ModelQualificationGate.IntegratedHost;
        if (state is "missing-final-gate") { evidence &= ~ModelQualificationGate.DotNetFinalRequest; }
        if (state is "missing-account") { evidence &= ~ModelQualificationGate.ExecutionAccount; }
        if (state is "missing-host") { evidence &= ~ModelQualificationGate.IntegratedHost; }
        if (state is "claims-production") { evidence |= ModelQualificationGate.AllPathLifecycle; }
        var registration = new ModelProviderRegistration(
            state is "local" ? ModelProviderSelection.OllamaCandidate : ModelProviderSelection.CopilotCandidate,
            evidence, f.Time.Now.AddMinutes(1), f.Adapter, () => state is not "revoked");
        registration.IsQualified(f.Time.Now).Should().Be(admitted);
    }

    [Theory]
    [InlineData("current", ModelTurnOutcome.Succeeded)]
    [InlineData("missing", ModelTurnOutcome.Unavailable)]
    [InlineData("foreign-selection", ModelTurnOutcome.Unavailable)]
    [InlineData("unqualified", ModelTurnOutcome.Unavailable)]
    [InlineData("revoked", ModelTurnOutcome.Unavailable)]
    [InlineData("owner-lost", ModelTurnOutcome.Denied)]
    [InlineData("cancelled", ModelTurnOutcome.Cancelled)]
    [InlineData("context-expired", ModelTurnOutcome.Denied)]
    [InlineData("task-changed", ModelTurnOutcome.Denied)]
    public async Task DynamicRegistrationRequiresTheExactProfileAndIndependentCurrentQualification(string state, ModelTurnOutcome expected)
    {
        using var f = new Fixture();
        using var root = f.Root();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var source = new RegistrationSource
        {
            Resolve = (selection, profile, _) =>
            {
                profile.Should().Be(Profile());
                selection.Should().Be(ModelProviderSelection.OllamaCandidate);
                if (state is "owner-lost") { f.Current = false; }
                if (state is "cancelled") { cancellation.Cancel(); }
                if (state is "context-expired") { f.Time.Now = f.Time.Now.AddMinutes(1); }
                if (state is "task-changed") { f.Task = f.Task! with { Generation = new(2) }; }
                return Task.FromResult(state is "missing" ? null : new ModelProviderRegistration(
                    state is "foreign-selection" ? selection with { Revision = new(2) } : selection,
                    state is "unqualified" ? ModelQualificationGate.None : Fixture.Gates(selection),
                    f.Time.Now.AddMinutes(1), f.Adapter, () => state is not "revoked"));
            },
        };
        var host = f.ConfiguredProfiles(registrationSource: source);
        var admission = await host.AdmitAsync(ModelTurnChoice.Local, f.Context(), null, cancellation.Token);
        admission.Result.Outcome.Should().Be(expected);
        source.Calls.Should().Be(1);
        if (expected == ModelTurnOutcome.Succeeded)
        { (await host.RunAsync(admission.Turn!, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded); }
        else { admission.Turn.Should().BeNull(); }
        f.Adapter.Calls.Should().Be(expected == ModelTurnOutcome.Succeeded ? 1 : 0);
    }

    [Fact]
    public async Task QualificationRevocationAfterAdmissionSuppressesDispatchAndRetainedOutput()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var current = true;
        var source = new RegistrationSource
        {
            Resolve = (selection, _, _) => Task.FromResult<ModelProviderRegistration?>(new(selection,
                Fixture.Gates(selection), f.Time.Now.AddMinutes(1), f.Adapter, () => current)),
        };
        var host = f.ConfiguredProfiles(registrationSource: source);
        var admitted = await host.AdmitAsync(ModelTurnChoice.Local, f.Context(), null, Token);
        current = false;
        (await host.RunAsync(admitted.Turn!, Token)).Reason.Should().Be(ModelTurnReason.QualificationPending);
        f.Adapter.Calls.Should().Be(0);
        current = true;
        admitted = await host.AdmitAsync(ModelTurnChoice.Local, f.Context(), null, Token);
        f.Adapter.OnRun = () => current = false;
        var result = await host.RunAsync(admitted.Turn!, Token);
        result.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
        result.Response.Should().BeNull();
        f.Adapter.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RegistrationSourceFailuresAreExplicitAndCannotFallBackToStaticCandidate()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var source = new RegistrationSource { Resolve = (_, _, _) => throw new IOException("private ownership failure") };
        var host = f.ConfiguredProfiles(registrationSource: source);
        await host.Invoking(value => value.AdmitAsync(ModelTurnChoice.Local, f.Context(), null, Token)).Should().ThrowAsync<IOException>();
        f.Adapter.Calls.Should().Be(0);
        f.Messages.Should().NotContain(message => message.Contains("private ownership", StringComparison.Ordinal));
    }

    private sealed class RegistrationSource : IModelProviderRegistrationSource
    {
        internal int Calls;
        internal required Func<ModelProviderSelection, ProviderProfileTuple, CancellationToken, Task<ModelProviderRegistration?>> Resolve;
        public Task<ModelProviderRegistration?> ResolveAsync(ModelProviderSelection selection, ProviderProfileTuple profile,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Resolve(selection, profile, cancellationToken);
        }
    }
}
