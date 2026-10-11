using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class ProviderProfileFieldConfigurationTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ProviderProfileFieldConfigurationTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    internal static ProviderProfileTuple BaseTuple(ProviderService service = ProviderService.Local) =>
        new(service, new("account"), new(1), new("exact-model"), new("managed-effort"), new("managed-window"));
    internal static string Replacement(ProviderProfileField field) => field switch
    {
        ProviderProfileField.Model => "compatible-model",
        ProviderProfileField.Reasoning => "exact-effort",
        _ => "exact-window",
    };

    [Theory]
    [InlineData(ProviderProfileField.Model)]
    [InlineData(ProviderProfileField.Reasoning)]
    [InlineData(ProviderProfileField.Context)]
    public async Task IndividualFieldReviewUsesOneAtomicCompleteTupleAndPreservesDefaultAndOtherServices(ProviderProfileField field)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        f.Catalogue.FieldCatalogue = true;
        f.Preferences.Value = new(ProviderService.Unset, [BaseTuple(), BaseTuple(ProviderService.Claude)]);
        await f.Refresh();
        var selected = f.Service.Choices.Single(choice => choice.Service == ProviderService.Local);
        var preview = f.Service.CreateFieldChoice(field, new(Replacement(field)), selected);
        preview.Confirmation.Should().NotBe(Guid.Empty);
        preview.Field.Should().Be(field);
        preview.BaseTuple.Should().Be(BaseTuple());
        f.Preferences.Writes.Should().Be(0);
        (await f.Apply(preview)).Should().BeTrue();
        f.Preferences.Value!.Service.Should().Be(ProviderService.Unset);
        f.Preferences.Value.For(ProviderService.Claude).Should().Be(BaseTuple(ProviderService.Claude));
        f.Preferences.Value.For(ProviderService.Local).Should().Be(ProviderProfileRules.ReplaceField(BaseTuple(), field, new(Replacement(field))));
        f.Preferences.Writes.Should().Be(1);
        f.Preferences.Pending.Should().BeFalse();
        f.Audit.Events.Select(audit => audit.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Hosts.Should().HaveCount(2).And.OnlyContain(host => host != null && host.SessionId == f.Store.Authority!.SessionId);
        f.Audit.Events[0].CorrelationId.Should().Be(f.Audit.Events[1].CorrelationId);
        f.Audit.Events.Should().OnlyContain(audit => string.Equals(audit.ActionId,
            "configuration.provider-profile." + field.ToString().ToLowerInvariant(), StringComparison.Ordinal));
        f.Service.Get().ExecutionEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unset")]
    [InlineData("incompatible")]
    [InlineData("unknown-id")]
    [InlineData("foreign")]
    [InlineData("expired")]
    [InlineData("retired")]
    [InlineData("account")]
    [InlineData("catalogue")]
    [InlineData("changed-save")]
    [InlineData("missing-installation")]
    public async Task FieldReviewRejectsIncompleteOrStaleBasesAndNeverPicksDefaultDependents(string state)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        f.Catalogue.FieldCatalogue = true;
        var tuple = BaseTuple();
        if (state is "retired") { tuple = tuple with { Model = new("retired") }; }
        if (state is "account") { tuple = tuple with { Account = new("other") }; }
        if (state is "catalogue") { tuple = tuple with { CatalogueRevision = new(2) }; }
        f.Preferences.Value = state is "missing" ? new(ProviderService.Local, []) : new(state is "unset" ? ProviderService.Unset : ProviderService.Local, [tuple]);
        if (state is "missing-installation") { f.Catalogue.Installation = LocalProviderInstallation.Missing; }
        await f.Refresh();
        ProviderProfileChoice? selected = null;
        if (state is "foreign")
        {
            var foreign = f.CreateService();
            await foreign.RefreshAsync(RequestOrigin.LocalUi, () => f.Eligible, f.Token);
            selected = foreign.Choices.Single(choice => choice.Service == ProviderService.Local);
        }
        if (state is "expired") { f.Time.Now = f.Catalogue.Expiry; }
        if (state is "changed-save") { f.Preferences.Value = new(ProviderService.Claude, [tuple]); }
        var before = f.Preferences.Value!.Serialize();
        FluentActions.Invoking(() => f.Service.CreateFieldChoice(ProviderProfileField.Model,
            new(state is "incompatible" ? "incompatible-model" : state is "unknown-id" ? "unknown" : "compatible-model"), selected))
            .Should().Throw<InvalidOperationException>();
        f.Preferences.Value.Serialize().Should().Be(before);
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        if (state is "missing" or "incompatible")
        {
            var model = f.Service.Models.Single(item => string.Equals(item.Id.Value, "incompatible-model", StringComparison.Ordinal));
            await f.Apply(f.Service.CreateTupleChoice(model, model.Reasoning[0], model.Context[0]));
            f.Preferences.Value.For(ProviderService.Local)!.Model.Should().Be(model.Id);
        }
    }

    [Theory]
    [InlineData("account")]
    [InlineData("catalogue")]
    [InlineData("expired")]
    [InlineData("missing")]
    [InlineData("retired")]
    [InlineData("save")]
    [InlineData("generation")]
    [InlineData("privacy")]
    [InlineData("host")]
    [InlineData("revision")]
    [InlineData("cancel")]
    [InlineData("call")]
    public async Task FieldConfirmationRevalidatesExactOriginalAdmissionAndCatalogue(string change)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        f.Catalogue.FieldCatalogue = true;
        f.Preferences.Value = new(ProviderService.Local, [BaseTuple()]);
        await f.Refresh();
        var preview = f.Service.CreateFieldChoice(ProviderProfileField.Model, new("compatible-model"));
        var call = f.Policy.Current.Revision;
        if (change is "account") { f.Catalogue.Account = "changed"; }
        if (change is "catalogue") { f.Catalogue.Revision++; }
        if (change is "expired") { f.Time.Now = f.Catalogue.Expiry; }
        if (change is "missing") { f.Catalogue.Installation = LocalProviderInstallation.Unknown; }
        if (change is "retired") { f.Catalogue.FieldCatalogue = false; }
        if (change is "save") { f.Preferences.Value = new(ProviderService.Unset, [BaseTuple()]); }
        if (change is "generation") { f.Store.Authority = f.Store.Authority! with { Generation = new(2) }; }
        if (change is "privacy") { f.Store.Authority = null; }
        if (change is "host") { f.Eligible = false; }
        if (change is "revision") { await f.Refresh(); }
        if (change is "call") { f.Call.State = CallState.Unknown; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (change is "cancel") { await cancellation.CancelAsync(); }
        if (change is "call") { (await f.Apply(preview, callRevision: call)).Should().BeFalse(); }
        else { await FluentActions.Awaiting(() => f.Apply(preview, token: cancellation.Token)).Should().ThrowAsync<Exception>(); }
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("save")]
    [InlineData("readback")]
    [InlineData("confirm")]
    [InlineData("receipt")]
    public async Task FieldWriteFailureKeepsExistingUnconfirmedFenceAndCannotClaimSuccess(string failure)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        f.Catalogue.FieldCatalogue = true;
        f.Preferences.Value = new(ProviderService.Local, [BaseTuple()]);
        await f.Refresh();
        var preview = f.Service.CreateFieldChoice(ProviderProfileField.Context, new("exact-window"));
        if (failure is "save") { f.Preferences.SaveFailure = new IOException(); }
        if (failure is "readback") { f.Preferences.AfterWrite = () => f.Preferences.Value = new(ProviderService.Unset, []); }
        if (failure is "confirm") { f.Preferences.ConfirmFailure = new IOException(); }
        if (failure is "receipt") { f.Store.FailTerminal = true; }
        f.Audit.BeforeWrite = audit =>
        {
            if (failure is "requested-audit" && audit.Outcome == SecurityAuditOutcome.Requested
                || failure is "terminal-audit" && audit.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        await FluentActions.Awaiting(() => f.Apply(preview)).Should().ThrowAsync<Exception>();
        f.Service.Get().ConfigurationAvailable.Should().BeFalse();
        if (failure is not "requested-audit") { f.Preferences.Pending.Should().BeTrue(); }
    }

    [Fact]
    public async Task FieldPreviewCapacityIsBoundedWithoutEviction()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        f.Catalogue.FieldCatalogue = true;
        f.Preferences.Value = new(ProviderService.Local, [BaseTuple()]);
        await f.Refresh();
        for (var index = 0; index < 59; index++) { f.Service.CreateFieldChoice(ProviderProfileField.Model, new("compatible-model")); }
        FluentActions.Invoking(() => f.Service.CreateFieldChoice(ProviderProfileField.Model, new("compatible-model"))).Should().Throw<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FieldAuditRetainsHostSessionAndTraceAndTerminatesTruthfully(bool fail)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        f.Catalogue.FieldCatalogue = true;
        f.Preferences.Value = new(ProviderService.Local, [BaseTuple()]);
        await f.Refresh();
        var preview = f.Service.CreateFieldChoice(ProviderProfileField.Reasoning, new("exact-effort"));
        var audits = new List<(SecurityAuditEvent Audit, HostRequest Request, Activity Activity)>();
        f.Audit.BeforeWrite = audit => audits.Add((audit, HostActivity.RequireCurrent().Request, Activity.Current!));
        if (fail) { f.Preferences.SaveFailure = new IOException("atomic failure"); }
        if (fail) { await FluentActions.Awaiting(() => f.Apply(preview)).Should().ThrowAsync<IOException>(); }
        else { await f.Apply(preview); }
        audits.Should().HaveCount(2);
        audits[0].Audit.Outcome.Should().Be(SecurityAuditOutcome.Requested);
        audits[1].Audit.Outcome.Should().Be(fail ? SecurityAuditOutcome.Failed : SecurityAuditOutcome.Succeeded);
        audits[1].Audit.CorrelationId.Should().Be(audits[0].Audit.CorrelationId);
        audits[1].Request.Should().BeSameAs(audits[0].Request);
        audits[1].Activity.TraceId.Should().Be(audits[0].Activity.TraceId);
        audits[1].Request.SessionId.Should().Be(f.Store.Authority!.SessionId);
        audits[1].Activity.IsStopped.Should().BeTrue();
        audits[1].Activity.Status.Should().Be(fail ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        audits[1].Activity.Baggage.Should().BeEmpty();
        audits[1].Activity.TagObjects.Should().NotContain(tag => tag.Value != null && tag.Value.ToString()!.Contains("exact-effort", StringComparison.Ordinal));
    }
}
