using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(ProviderProfileField.Model)]
    [InlineData(ProviderProfileField.Reasoning)]
    [InlineData(ProviderProfileField.Context)]
    public async Task RealAppFieldWriterChangesOnlyLaterInheritedTurnsAndPreservesExplicitSessionFields(ProviderProfileField field)
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfileCatalogue.FieldCatalogue = true;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        var captured = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        captured.Turn!.Profile.Should().Be(Profile());
        using var call = new Kora.Application.Communication.CallCommunicationPolicy(
            new Kora.Application.UnitTests.Configuration.ProviderProfileConfigurationFixture.ProfileCall());
        f.ProfileCatalogue.BeforeObservation = () =>
        {
            f.ExpectedAuditRequest = HostActivity.RequireCurrent().Request;
            return System.Threading.Tasks.Task.CompletedTask;
        };
        await f.ProfileConfiguration.RefreshAsync(f.Request.Origin, () => f.Current, Token);
        var replacement = Kora.Application.UnitTests.Configuration.ProviderProfileFieldConfigurationTests.Replacement(field);
        var preview = f.ProfileConfiguration.CreateFieldChoice(field, new(replacement));
        await f.ProfileConfiguration.SelectAsync(preview, call.Current.Revision, f.Request.Origin,
            SecurityAuditInitiator.TypedCommand, call, () => f.Current, Token);
        f.ProfileCatalogue.BeforeObservation = null;
        f.ExpectedAuditRequest = null;
        var updated = ProviderProfileRules.ReplaceField(Profile(), field, new(replacement));
        f.ProfilePreferences.Value!.For(ProviderService.Local).Should().Be(updated);
        (await host.RunAsync(captured.Turn, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Adapter.Profile.Should().Be(Profile());
        var next = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        if (field == ProviderProfileField.Model)
        {
            // The test host has no independently qualified registration for the newly advertised model.
            next.Turn.Should().BeNull();
        }
        else { next.Turn!.Profile.Should().Be(updated); }
        var sessionField = field switch
        {
            ProviderProfileField.Model => SessionProviderField.Model,
            ProviderProfileField.Reasoning => SessionProviderField.Reasoning,
            _ => SessionProviderField.Context,
        };
        f.SessionOverrides = SessionProviderOverrides.Inherit.Select(Profile(), sessionField);
        var unchangedSession = f.SessionOverrides.Serialize();
        var overridden = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        overridden.Turn!.Profile.Should().Be(Profile());
        f.SessionOverrides.Serialize().Should().Be(unchangedSession);
        f.Adapter.Calls.Should().Be(1);
        f.ProviderPreferences.Mode.Should().BeNull();
    }

    [Fact]
    public async Task MissingSessionPolicyAuthorityNeverFallsBackToApp()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        (await f.ConfiguredProfiles(missingSessionStore: true).AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token))
            .Result.Reason.Should().Be(ModelTurnReason.SessionNotAdmitted);
        f.Adapter.Calls.Should().Be(0);
    }
    [Fact]
    public async Task SessionPolicyCaptureSurvivesLaterSessionAndAppEditsWithoutRerouteOrCancellation()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Unset, [Profile()]);
        f.SessionOverrides = new(ProviderService.Local);
        f.SessionProfileRevision = 7;
        var host = f.ConfiguredProfiles();
        var admitted = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        admitted.Turn!.Profile.Should().Be(Profile());
        admitted.Turn.SessionPolicyRevision.Should().Be(7);
        f.SessionOverrides = new(ProviderService.Claude);
        f.SessionProfileRevision = 8;
        f.ProfilePreferences.Value = new(ProviderService.Codex, [Profile(), Profile(ProviderService.Claude)]);
        (await host.RunAsync(admitted.Turn, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Adapter.Profile.Should().Be(Profile());
        (await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token)).Result.Reason.Should().Be(ModelTurnReason.InvalidPolicy);
        f.Adapter.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("held")]
    [InlineData("done")]
    [InlineData("changed")]
    public async Task UnknownSessionPolicyOrChangedAdmissionSnapshotFailsClosed(string state)
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        if (state is "held") { f.SessionProfileHeld = true; }
        if (state is "done") { f.Session = f.Session with { IsActive = false }; }
        if (state is "changed") { f.AfterProfileRead = () => f.SessionProfileRevision++; }
        var admission = await f.ConfiguredProfiles().AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        admission.Turn.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PrivateSessionPolicyAuthorityLossBeforeDispatchPreventsCapturedTurnWithoutFallback()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        var admitted = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        f.SessionProfileHeld = true;
        (await host.RunAsync(admitted.Turn!, Token)).Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        f.Adapter.Calls.Should().Be(0);
    }

    private static ProviderProfileTuple Profile(ProviderService service = ProviderService.Local) =>
        new(service, new("account"), new(1), new("exact-model"), new("managed-effort"), new("managed-window"));

    [Theory]
    [InlineData(LocalProviderInstallation.Installed)]
    [InlineData(LocalProviderInstallation.Missing)]
    [InlineData(LocalProviderInstallation.Unknown)]
    public async Task ComposedDefaultNeverInventsAQualifiedModelFromInstallation(LocalProviderInstallation installation)
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Installation = installation;
        var host = f.ConfiguredProfiles(production: true);
        var admission = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        admission.Result.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        admission.Turn.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task NextTurnResolvesLatestAppTupleButDoesNotMutateOrCancelAnAdmittedTurn()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        var admitted = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        admitted.Turn!.Profile.Should().Be(Profile());
        f.ProfilePreferences.Value = new(ProviderService.Claude, [Profile(), Profile(ProviderService.Claude)]);
        (await host.RunAsync(admitted.Turn, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        f.Adapter.Profile.Should().Be(Profile());
        f.Adapter.Provenance!.Selection.Provider.Should().Be(ModelProviderIdentity.Ollama);
        var subsequent = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        subsequent.Result.Reason.Should().Be(ModelTurnReason.InvalidPolicy);
        f.Adapter.Calls.Should().Be(1);
        f.ProviderPreferences.Mode.Should().BeNull();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("catalogue")]
    [InlineData("expiry")]
    [InlineData("installation")]
    public async Task AccountCatalogueOrInstallationLossBeforeDispatchFailsWithoutFallback(string field)
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        var admitted = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        if (field is "account") { f.ProfileCatalogue.Account = "other"; }
        if (field is "catalogue") { f.ProfileCatalogue.Revision = 2; }
        if (field is "expiry") { f.ProfileCatalogue.Expiry = DateTimeOffset.UnixEpoch; }
        if (field is "installation") { f.ProfileCatalogue.Installation = LocalProviderInstallation.Unknown; }
        (await host.RunAsync(admitted.Turn!, Token)).Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(ProviderService.Copilot)]
    [InlineData(ProviderService.Claude)]
    [InlineData(ProviderService.Codex)]
    public async Task ExplicitHostedTupleStillRequiresModeQualificationAndEgressAuthority(ProviderService service)
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(service, [Profile(service)]);
        f.ProviderPreferences.Mode = ModelProviderMode.HostedPreferred;
        var host = f.ConfiguredProfiles();
        var result = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        result.Result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        result.Result.Provenance!.Selection.Provider.Should().Be(service switch
        {
            ProviderService.Copilot => ModelProviderIdentity.Copilot,
            ProviderService.Claude => ModelProviderIdentity.Claude,
            _ => ModelProviderIdentity.Codex,
        });
        result.Turn.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
        f.ProviderPreferences.Mode = ModelProviderMode.LocalFirst;
        var firstHost = f.ConfiguredProfiles();
        (await firstHost.AdmitAsync(ModelTurnChoice.Hosted, f.Context(), null, Token)).Result.Reason.Should().Be(ModelTurnReason.HandoffReviewRequired);
        (await firstHost.AdmitAsync(ModelTurnChoice.Local, f.Context(), null, Token)).Result.Reason.Should().Be(ModelTurnReason.InvalidPolicy);
    }

    [Theory]
    [InlineData(ModelTurnChoice.Hosted)]
    [InlineData((ModelTurnChoice)99)]
    public async Task ExplicitMismatchedOrUnknownTurnChoiceCannotSelectAnotherService(ModelTurnChoice choice)
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        (await host.AdmitAsync(choice, f.Context(), null, Token)).Result.Reason.Should().Be(ModelTurnReason.InvalidPolicy);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("origin")]
    [InlineData("session")]
    [InlineData("audit")]
    [InlineData("pending")]
    [InlineData("cancel")]
    [InlineData("changed")]
    [InlineData("publication")]
    public async Task AppProfileAdmissionNeverBypassesCurrentHostSessionIntentAndAudit(string field)
    {
        using var f = new Fixture(field is "origin" ? Kora.Core.Hosting.RequestOrigin.HostSystem : Kora.Core.Hosting.RequestOrigin.LocalUi);
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        if (field is "host") { f.Current = false; }
        if (field is "session") { f.Session = f.Session with { IsActive = false }; }
        if (field is "pending") { f.ProfilePreferences.Pending = true; }
        using var cancelled = new CancellationTokenSource();
        f.ProfileCatalogue.AfterObservation = () =>
        {
            if (field is "cancel") { cancelled.Cancel(); }
        };
        f.OnAudit = audit =>
        {
            if (field is "audit" && string.Equals(audit.ActionId, "model.app-profile", StringComparison.Ordinal) && audit.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException(); }
            if (field is "changed" && string.Equals(audit.ActionId, "model.turn", StringComparison.Ordinal) && audit.Outcome == SecurityAuditOutcome.Succeeded)
            { f.ProfilePreferences.Value = new(ProviderService.Unset, []); }
            if (field is "publication" && string.Equals(audit.ActionId, "model.app-profile", StringComparison.Ordinal) && audit.Outcome == SecurityAuditOutcome.Succeeded)
            { f.Current = false; }
        };
        if (field is "audit" or "pending" or "cancel")
        {
            if (field is "cancel")
            {
                (await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, cancelled.Token)).Result.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
            }
            else
            {
                await host.Invoking(item => item.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token)).Should().ThrowAsync<Exception>();
            }
        }
        else
        {
            (await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token)).Turn.Should().BeNull();
        }
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("completed")]
    [InlineData("stopped")]
    public async Task AppProfileEntryRequiresAnUnexpiredHostIssuedActivity(string field)
    {
        using var f = new Fixture();
        var host = f.ConfiguredProfiles();
        if (field is "missing")
        {
            (await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token)).Result.Reason.Should().Be(ModelTurnReason.HostContextRequired);
            return;
        }
        using var root = f.Root();
        if (field is "completed") { root.Complete(HostOperationOutcome.Completed); }
        else { root.Activity!.Stop(); System.Diagnostics.Activity.Current = root.Activity; }
        (await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token)).Result.Reason.Should().Be(ModelTurnReason.HostContextRequired);
    }

    [Fact]
    public async Task DriftDuringExactSavedReadCannotIssueATurnAndObservationPrivacyLossPreventsDispatch()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        f.ProfilePreferences.AfterRead = () => f.ProfilePreferences.Value = new(ProviderService.Unset, []);
        var stale = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        stale.Result.Reason.Should().Be(ModelTurnReason.PolicyChanged);
        f.ProfilePreferences.AfterRead = null;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var admitted = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        f.ProfileCatalogue.AfterObservation = () => f.Current = false;
        (await host.RunAsync(admitted.Turn!, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ReviewFromAnotherHostPolicyCannotAuthorizeAnAppHostedProfile()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var policy = await Configure(f);
        var offer = await Offer(f, policy);
        var reviewed = await f.Workflow.ReviewAsync(offer, policy, offer.Context, ModelHandoffDecision.Approve,
            Kora.Core.Hosting.RequestOrigin.LocalUi, Token);
        reviewed.Review.Should().NotBeNull();
        f.ProviderPreferences.Mode = ModelProviderMode.LocalFirst;
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Copilot, [Profile(ProviderService.Copilot)]);
        var host = f.ConfiguredProfiles();
        (await host.AdmitAsync(ModelTurnChoice.Hosted, f.Context(), reviewed.Review, Token)).Result.Reason.Should().Be(ModelTurnReason.HandoffReviewStale);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task CancellationDuringTerminalProfileAuditSuppressesTheJustAdmittedCapability()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        using var cancellation = new CancellationTokenSource();
        f.OnAudit = audit =>
        {
            if (string.Equals(audit.ActionId, "model.app-profile", StringComparison.Ordinal) && audit.Outcome == SecurityAuditOutcome.Succeeded)
            { cancellation.Cancel(); }
        };
        var host = f.ConfiguredProfiles();
        var result = await host.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, cancellation.Token);
        result.Result.Outcome.Should().Be(ModelTurnOutcome.Cancelled);
        result.Turn.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }
}
