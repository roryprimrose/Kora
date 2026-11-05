using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class ProviderSettingsToolTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ProviderSettingsToolTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task DiscoveryDetailsRefreshAndProposalsNeverPersistOrAcquireAuthority()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var settings = await Invoke(f, ProviderSettingsTools.Get, "{}");
        settings.ProviderSettings!.Settings!.Desired.Service.Should().Be(ProviderService.Local);
        settings.ProviderSettings.Settings.IsSaved.Should().BeFalse();
        settings.ProviderSettings.Settings.Services.Should().HaveCount(4);
        var models = await Invoke(f, ProviderSettingsTools.Models, """{"service":"Claude","offset":0,"count":1}""");
        models.ProviderSettings!.Models!.Records.Should().ContainSingle().Which.Id.Value.Should().Be("exact-model");
        models.ProviderSettings.Models.NextOffset.Should().BeNull();
        var model = await Invoke(f, ProviderSettingsTools.Model, """{"service":"Claude","model":"exact-model"}""");
        model.ProviderSettings!.Model!.Model.Reasoning.Single().Id.Value.Should().Be("managed-effort");
        model.ProviderSettings.Model.Model.Context.Single().Id.Value.Should().Be("managed-window");
        var refresh = await Invoke(f, ProviderSettingsTools.Refresh, """{"service":"Claude"}""");
        refresh.ProviderSettings!.Service!.Status.Should().Be(ProviderCatalogueStatus.Current);
        f.Catalogue.Refreshes.Should().Equal(ProviderService.Claude);
        var proposal = await Invoke(f, ProviderSettingsTools.Propose,
            """{"service":"Claude","model":"exact-model","reasoning":"managed-effort","context":"managed-window","account":"account","revision":1}""");
        proposal.ProviderSettings!.Proposal!.For(ProviderService.Claude)!.Model.Value.Should().Be("exact-model");
        proposal.ProviderSettings.RequiresOriginalUserConfirmation.Should().BeTrue();
        proposal.ProviderSettings.PreferenceWritten.Should().BeFalse();
        foreach (var reply in new[] { settings, models, model, refresh, proposal })
        { reply.Outcome.Should().Be(CapabilityOutcome.Succeeded); }
        f.Preferences.Value.Should().BeNull();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        f.Service.Get().ConfigurationAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData(ProviderCatalogueStatus.Unavailable)]
    [InlineData(ProviderCatalogueStatus.AuthenticationRequired)]
    public async Task MissingServicesRetainDesiredChoiceReturnExplicitStateAndNeverInventModels(ProviderCatalogueStatus status)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = status;
        f.Preferences.Value = new(ProviderService.Claude, []);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Application, HostOperation.Request);
        var result = await Invoke(f, ProviderSettingsTools.Models, """{"service":"Claude"}""");
        result.ProviderSettings!.Models!.Service.Status.Should().Be(status);
        result.ProviderSettings.Models.Records.Should().BeEmpty();
        var refresh = await Invoke(f, ProviderSettingsTools.Refresh, """{"service":"Claude"}""");
        refresh.ProviderSettings!.Service!.Status.Should().Be(status);
        var proposal = await Invoke(f, ProviderSettingsTools.Propose, """{"service":"Copilot"}""");
        proposal.ProviderSettings!.Proposal!.Service.Should().Be(ProviderService.Copilot);
        f.Preferences.Value.Service.Should().Be(ProviderService.Claude);
        f.Preferences.Writes.Should().Be(0);
        await f.Service.Invoking(service => service.InvokeToolAsync(ProviderSettingsTools.Model,
            """{"service":"Claude","model":"exact-model"}""", () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("revision")]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    [InlineData("installation")]
    [InlineData("expiry")]
    public async Task ProposedTuplesMustMatchEveryCurrentAdvertisedValue(string change)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        if (change is "account") { f.Catalogue.Account = "other"; }
        if (change is "revision") { f.Catalogue.Revision = 2; }
        if (change is "model") { f.Catalogue.Model = "other"; }
        if (change is "reasoning") { f.Catalogue.Reasoning = "other"; }
        if (change is "context") { f.Catalogue.Context = "other"; }
        if (change is "installation") { f.Catalogue.Installation = LocalProviderInstallation.Missing; }
        if (change is "expiry") { f.Time.Now = f.Catalogue.Expiry; }
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.InvokeToolAsync(ProviderSettingsTools.Propose,
            """{"service":"Local","model":"exact-model","reasoning":"managed-effort","context":"managed-window","account":"account","revision":1}""",
            () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task LossOfAdmissionOrPreferenceChangeDuringObservationSuppressesAllDisclosure()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        f.Catalogue.AfterObservation = () => f.Eligible = false;
        var denied = await f.Service.InvokeToolAsync(ProviderSettingsTools.Get, "{}", () => f.Eligible, f.Token);
        denied.Outcome.Should().Be(CapabilityOutcome.Denied);
        denied.ProviderSettings.Should().BeNull();
        f.Eligible = true;
        f.Catalogue.AfterObservation = () => f.Preferences.Value = new(ProviderService.Claude, []);
        var changed = await f.Service.InvokeToolAsync(ProviderSettingsTools.Get, "{}", () => f.Eligible, f.Token);
        changed.Outcome.Should().Be(CapabilityOutcome.Denied);
        changed.ProviderSettings.Should().BeNull();
        var bad = await f.Service.InvokeToolAsync(ProviderSettingsTools.Refresh, """{"service":"Claude","credential":"forbidden"}""", () => true, f.Token);
        bad.Outcome.Should().Be(CapabilityOutcome.Denied);
        var unknown = await f.Service.InvokeToolAsync("providers.unknown", "{}", () => true, f.Token);
        unknown.Outcome.Should().Be(CapabilityOutcome.Denied);
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ToolProposalRequiresFreshOriginalReviewAndSeparateAuditedConfirmation()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            await Invoke(f, ProviderSettingsTools.Propose,
                """{"service":"Claude","model":"exact-model","reasoning":"managed-effort","context":"managed-window","account":"account","revision":1}""");
        }
        f.Service.Suggested!.Service.Should().Be(ProviderService.Claude);
        f.Preferences.Writes.Should().Be(0);
        await f.Refresh();
        var preview = f.Service.CreateSuggestionChoice();
        preview.Confirmation.Should().NotBeEmpty();
        f.Service.Suggested.Should().BeNull();
        f.Preferences.Writes.Should().Be(0);
        (await f.Apply(preview)).Should().BeTrue();
        f.Preferences.Value!.Service.Should().Be(ProviderService.Claude);
        f.Preferences.Value.For(ProviderService.Claude)!.Model.Value.Should().Be("exact-model");
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(Kora.Core.Auditing.SecurityAuditOutcome.Requested,
            Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
        await f.Service.Invoking(service => f.Apply(preview)).Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(1);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("preferences")]
    [InlineData("catalogue")]
    [InlineData("installation")]
    [InlineData("discarded")]
    public async Task StaleOrDiscardedSuggestionNeverBecomesWriteAuthority(string fault)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var target = fault is "installation" ? "Local" : "Claude";
        await Invoke(f, ProviderSettingsTools.Propose,
            $$"""{"service":"{{target}}","model":"exact-model","reasoning":"managed-effort","context":"managed-window","account":"account","revision":1}""");
        if (fault is "expired") { f.Time.Now = f.Time.Now.AddMinutes(5); }
        if (fault is "preferences") { f.Preferences.Value = new(ProviderService.Copilot, []); }
        if (fault is "catalogue") { f.Catalogue.Revision = 2; }
        if (fault is "installation") { f.Catalogue.Installation = LocalProviderInstallation.Missing; }
        if (fault is "discarded") { f.Service.DiscardSuggestion(); }
        await f.Refresh();
        FluentActions.Invoking(() => f.Service.CreateSuggestionChoice()).Should().Throw<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ProposalPublicationRevalidatesAdmissionAtEveryDisclosureAndPublicationBoundary(int revokeAt)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var reads = 0;
        var reply = await f.Service.InvokeToolAsync(ProviderSettingsTools.Propose, """{"service":"Claude"}""",
            () => ++reads < revokeAt, f.Token);
        reply.Outcome.Should().Be(CapabilityOutcome.Denied);
        reply.ProviderSettings.Should().BeNull();
        f.Service.Suggested.Should().BeNull();
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("invocation")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("cancelled")]
    public async Task ForeignOrRetiredSourcesCannotDiscloseProviderSettings(string fault)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
            fault is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
            fault is "invocation" ? new HostId<InvocationIdentity>(Guid.NewGuid()) : null);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (fault is "stopped") { f.Catalogue.AfterObservation = root.Activity!.Stop; }
        if (fault is "completed") { f.Catalogue.AfterObservation = () => root.Complete(HostOperationOutcome.Completed); }
        if (fault is "cancelled") { f.Time.AfterRead = cancellation.Cancel; }
        var reply = await f.Service.InvokeToolAsync(ProviderSettingsTools.Get, "{}", () => true, cancellation.Token);
        reply.Outcome.Should().Be(CapabilityOutcome.Denied);
        reply.ProviderSettings.Should().BeNull();
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CancelledSuggestionPublicationAndForeignWithdrawalDoNotAffectPreferencesOrOtherSources()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        await Invoke(f, ProviderSettingsTools.Propose, """{"service":"Claude"}""");
        f.Service.WithdrawSuggestion(HostRequest.Create(RequestOrigin.LocalUi));
        f.Service.Suggested.Should().NotBeNull();
        f.Service.SuggestionChanged += (_, _) =>
        {
            if (f.Service.Suggested is not null) { f.Service.DiscardSuggestion(); }
            f.Eligible = false;
        };
        var reply = await f.Service.InvokeToolAsync(ProviderSettingsTools.Propose, """{"service":"Copilot"}""", () => f.Eligible, f.Token);
        reply.Outcome.Should().Be(CapabilityOutcome.Denied);
        f.Service.Suggested.Should().BeNull();
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task OversizedCompleteModelDetailsFailWithoutTruncationOrSuggestion()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        f.Catalogue.LargeCatalogue = true;
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var reply = await f.Service.InvokeToolAsync(ProviderSettingsTools.Model,
            """{"service":"Local","model":"exact-model0"}""", () => true, f.Token);
        reply.Outcome.Should().Be(CapabilityOutcome.Failed);
        reply.Reason.Should().Be("provider-settings-output-limit-exceeded");
        reply.ProviderSettings.Should().BeNull();
        f.Preferences.Writes.Should().Be(0);
    }

    private static async Task<CapabilityReply> Invoke(ProviderProfileConfigurationFixture f, string id, string input)
    {
        var reply = await f.Service.InvokeToolAsync(id, input, () => f.Eligible, f.Token);
        if (reply.Outcome == CapabilityOutcome.Succeeded)
        {
            var message = (ModelAssistantToolCalls)ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(
                $$"""{"kind":"toolCalls","calls":[{"callId":"provider_1","capabilityId":"{{id}}","input":{{input}}}]}"""));
            ModelToolProtocol.CreateToolResult(message.Calls.Single(), reply).CapabilityId.Should().Be(id);
        }
        return reply;
    }
}
