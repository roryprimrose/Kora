using System.Diagnostics;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class ProviderProfileConfigurationServiceTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ProviderProfileConfigurationServiceTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(LocalProviderInstallation.Installed, ProviderService.Local)]
    [InlineData(LocalProviderInstallation.Missing, ProviderService.Unset)]
    [InlineData(LocalProviderInstallation.Unknown, ProviderService.Unset)]
    public async Task InitialDesiredServiceUsesInstallationOnlyAndInventsNoModels(LocalProviderInstallation installation, ProviderService desired)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Service.Get().ConfigurationAvailable.Should().BeFalse();
        f.Service.Get().Installation.Should().Be(LocalProviderInstallation.Unknown);
        f.Service.Models.Should().BeEmpty();
        f.Catalogue.Installation = installation;
        await f.Refresh();
        f.Service.Get().Saved.Should().BeNull();
        f.Service.Get().Desired!.Service.Should().Be(desired);
        f.Service.Get().Effective.Should().BeNull();
        f.Service.Get().AuthenticationAvailable.Should().BeFalse();
        f.Service.Get().ExecutionEnabled.Should().BeFalse();
        f.Service.Get().Installation.Should().Be(installation);
        f.Service.Choices.Select(choice => choice.Label).Should().Equal("Unset", "Local", "Copilot", "Claude", "Codex");
        f.Service.Models.Should().BeEmpty();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Hosts.Should().OnlyContain(host => host != null);
    }

    [Theory]
    [InlineData(ProviderService.Unset)]
    [InlineData(ProviderService.Local)]
    [InlineData(ProviderService.Copilot)]
    [InlineData(ProviderService.Claude)]
    [InlineData(ProviderService.Codex)]
    public async Task ExplicitServicePersistsWithoutAuthenticationOrFallback(ProviderService desired)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        await f.Refresh();
        await f.Select(desired);
        f.Preferences.Pending.Should().BeFalse();
        f.Catalogue.Installation = LocalProviderInstallation.Missing;
        await f.Refresh();
        f.Service.Get().Saved!.Service.Should().Be(desired);
        f.Service.Get().Desired!.Service.Should().Be(desired);
        f.Service.Get().Effective.Should().BeNull();
        f.Service.Models.Should().BeEmpty();
        f.Audit.Events.Should().HaveCount(2);
        f.Audit.Events[0].Outcome.Should().Be(SecurityAuditOutcome.Requested);
        f.Audit.Events[1].Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
        f.Audit.Events[1].CorrelationId.Should().Be(f.Audit.Events[0].CorrelationId);
    }

    [Theory]
    [InlineData(ProviderService.Local)]
    [InlineData(ProviderService.Copilot)]
    [InlineData(ProviderService.Claude)]
    [InlineData(ProviderService.Codex)]
    public async Task CompleteCatalogueBackedTuplePreservesIndependentServiceSelectionsAndReset(ProviderService desired)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        await f.Refresh();
        await f.Select(desired);
        await f.Refresh();
        var choice = f.TupleChoice();
        await f.Apply(choice);
        f.Service.Get().Effective.Should().Be(choice.Tuple);
        f.Service.Get().ExecutionEnabled.Should().BeFalse();
        await f.Refresh();
        var other = desired == ProviderService.Codex ? ProviderService.Claude : ProviderService.Codex;
        await f.Select(other);
        await f.Refresh();
        await f.Apply(f.TupleChoice());
        await f.Refresh();
        await f.Apply(f.Service.CreateResetChoice(tupleOnly: true));
        f.Preferences.Value!.For(other).Should().BeNull();
        f.Preferences.Value.For(desired).Should().Be(choice.Tuple);
        await f.Refresh();
        await f.Apply(f.Service.CreateResetChoice(tupleOnly: false));
        f.Preferences.Value.Service.Should().Be(ProviderService.Local);
        f.Preferences.Value.For(desired).Should().Be(choice.Tuple);
        f.Catalogue.Installation = LocalProviderInstallation.Unknown;
        await f.Refresh();
        await f.Apply(f.Service.CreateResetChoice(tupleOnly: false));
        f.Preferences.Value.Service.Should().Be(ProviderService.Unset);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("lookalike")]
    [InlineData("revision")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("host")]
    [InlineData("origin")]
    [InlineData("saved")]
    [InlineData("held")]
    public async Task StalePreviewNeverWrites(string field)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        await f.Refresh();
        var choice = f.Service.Choices[3];
        if (field is "foreign")
        {
            var other = f.CreateService();
            await other.RefreshAsync(RequestOrigin.LocalUi, () => f.Eligible, f.Token);
            choice = other.Choices[3];
        }
        if (field is "lookalike") { choice = new(choice.Value, choice.Revision, choice.Owner, choice.Session, choice.Origin, choice.RemainsAdmitted); }
        if (field is "revision") { await f.Refresh(); }
        if (field is "session") { f.Store.Authority = f.Store.Authority! with { SessionId = new(Guid.NewGuid()) }; }
        if (field is "generation") { f.Store.Authority = f.Store.Authority! with { Generation = new(2) }; }
        if (field is "host") { f.Eligible = false; }
        if (field is "saved") { f.Preferences.Value = new(ProviderService.Copilot, []); }
        if (field is "held") { f.Service.HoldUnavailable(); }
        await FluentActions.Awaiting(() => f.Apply(choice, field is "origin" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        f.Service.Choices.Should().BeEmpty();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("revision")]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    [InlineData("expired")]
    [InlineData("unavailable")]
    public async Task CatalogueDriftPreservesDesiredTupleButClosesEffectiveStateAndSave(string field)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        await f.Refresh();
        var choice = f.TupleChoice();
        f.Preferences.Value = choice.Value;
        await f.Refresh();
        var next = f.TupleChoice();
        if (field is "account") { f.Catalogue.Account = "changed-account"; }
        if (field is "revision") { f.Catalogue.Revision = 2; }
        if (field is "model") { f.Catalogue.Model = "retired-replacement"; }
        if (field is "reasoning") { f.Catalogue.Reasoning = "different-effort"; }
        if (field is "context") { f.Catalogue.Context = "different-window"; }
        if (field is "expired") { f.Time.Now = f.Catalogue.Expiry; }
        if (field is "unavailable") { f.Catalogue.Status = ProviderCatalogueStatus.Unavailable; }
        await FluentActions.Awaiting(() => f.Apply(next)).Should().ThrowAsync<InvalidOperationException>();
        await f.Refresh();
        f.Service.Get().Desired!.For(ProviderService.Local).Should().Be(choice.Tuple);
        f.Service.Get().Effective.Should().BeNull();
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    [InlineData("held")]
    [InlineData("host")]
    public async Task DraftRequiresExactImmutableCatalogueReferences(string field)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        await f.Refresh();
        var model = f.Service.Models.Single();
        var reasoning = model.Reasoning[0];
        var context = model.Context[0];
        if (field is "model") { model = new(model.Id, model.Label, model.Selection, model.Reasoning, model.Context); }
        if (field is "reasoning") { reasoning = reasoning with { }; }
        if (field is "context") { context = context with { }; }
        if (field is "held") { f.Service.HoldUnavailable(); }
        if (field is "host") { f.Eligible = false; }
        FluentActions.Invoking(() => f.Service.CreateTupleChoice(model, reasoning, context)).Should().Throw<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("load")]
    [InlineData("save")]
    [InlineData("access")]
    [InlineData("readback")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("receipt")]
    [InlineData("confirm")]
    [InlineData("confirmed-readback")]
    [InlineData("confirmed-host")]
    [InlineData("confirmed-revision")]
    [InlineData("completion-account")]
    public async Task WriteAuditReceiptReadbackAndConfirmationFailuresCannotActivate(string field)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        await f.Refresh();
        var choice = f.TupleChoice();
        if (field is "load") { f.Preferences.LoadFailure = new InvalidDataException(); }
        if (field is "save") { f.Preferences.SaveFailure = new IOException(); }
        if (field is "access") { f.Preferences.SaveFailure = new UnauthorizedAccessException(); }
        if (field is "readback") { f.Preferences.AfterWrite = () => f.Preferences.Value = new(ProviderService.Unset, []); }
        if (field is "receipt") { f.Store.FailTerminal = true; }
        if (field is "confirm") { f.Preferences.ConfirmFailure = new IOException(); }
        f.Preferences.AfterConfirm = () =>
        {
            if (field is "confirmed-readback") { f.Preferences.Value = new(ProviderService.Unset, []); }
            if (field is "confirmed-host") { f.Eligible = false; }
            if (field is "confirmed-revision") { f.Service.HoldUnavailable(); }
        };
        f.Audit.BeforeWrite = audit =>
        {
            if (field is "requested-audit" && audit.Outcome == SecurityAuditOutcome.Requested
                || field is "terminal-audit" && audit.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        f.Store.BeforeCommit = task =>
        {
            if (task.State == HostTaskState.Succeeded && field is "completion-account") { f.Catalogue.Account = "other"; }
        };
        await FluentActions.Awaiting(() => f.Apply(choice)).Should().ThrowAsync<Exception>();
        f.Service.Get().ConfigurationAvailable.Should().BeFalse();
        f.Service.Get().Effective.Should().BeNull();
        if (field is not ("load" or "requested-audit")) { f.Preferences.Pending.Should().BeTrue(); }
    }

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Unknown)]
    public async Task ProtectedAndUnknownVoiceWritesAreDeniedWithoutAuthenticationOrSave(CallState state)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Call.State = state;
        await f.Refresh(RequestOrigin.ActivatedVoice);
        var choice = f.Service.Choices[3];
        (await f.Apply(choice, RequestOrigin.ActivatedVoice, initiator: SecurityAuditInitiator.VoiceCommand)).Should().BeFalse();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.System)]
    [InlineData(SecurityAuditInitiator.VoiceCommand)]
    public async Task ForgedInitiatorCannotWrite(SecurityAuditInitiator initiator)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        await f.Refresh();
        await FluentActions.Awaiting(() => f.Apply(f.Service.Choices[1], initiator: initiator)).Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task ModelAndExpiredHostCallbacksCannotRelabelSettingsAsOriginalUserInput()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
        await FluentActions.Awaiting(() => f.Refresh()).Should().ThrowAsync<InvalidOperationException>();
        f.Catalogue.Calls.Should().Be(0);
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CancellationAndObservationRevocationNeverPublishOrWrite()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await FluentActions.Awaiting(() => f.Service.RefreshAsync(RequestOrigin.LocalUi, () => true, cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
        f.Catalogue.AfterObservation = () => f.Eligible = false;
        await FluentActions.Awaiting(() => f.Refresh()).Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task AuthenticationRequiredIsExplicitAndExpiredCatalogueHasNoChoices()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Preferences.Value = new(ProviderService.Claude, []);
        f.Catalogue.Status = ProviderCatalogueStatus.AuthenticationRequired;
        await f.Refresh();
        f.Service.Get().Status.Should().Contain("AuthenticationRequired");
        f.Service.Models.Should().BeEmpty();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        await f.Refresh();
        f.Time.Now = f.Catalogue.Expiry;
        f.Service.Models.Should().BeEmpty();
    }

    [Fact]
    public async Task ReentrantOperationAndCompletionDriftFailClosed()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        Task? reentrant = null;
        f.Service.Changed += (_, _) => { reentrant ??= f.Refresh(); };
        await f.Refresh();
        await reentrant!.Invoking(async operation => await operation).Should().ThrowAsync<InvalidOperationException>();
        f.Store.BeforeCommit = task => { if (task.State == HostTaskState.Succeeded) { f.Preferences.Value = new(ProviderService.Copilot, []); } };
        await FluentActions.Awaiting(() => f.Refresh()).Should().ThrowAsync<InvalidOperationException>();
        f.Service.Get().ConfigurationAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedReceiptRevocationOrCancellationRetainsUnconfirmedFence(bool cancel)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        await f.Refresh();
        using var cancellation = new CancellationTokenSource();
        f.Store.BeforeCommit = task =>
        {
            if (task.State != HostTaskState.Succeeded) { return; }
            if (cancel) { cancellation.Cancel(); }
            else { f.Eligible = false; }
        };
        await FluentActions.Awaiting(() => f.Apply(f.Service.Choices[3], token: cancellation.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Pending.Should().BeTrue();
    }

    [Fact]
    public async Task CommittingDiscoveryRejectsDraftCreationAndConcurrentTurnResolution()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        await f.Refresh();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Catalogue.BeforeObservation = () => release.Task;
        var refresh = f.Refresh();
        FluentActions.Invoking(() => f.Service.CreateResetChoice(tupleOnly: false)).Should().Throw<InvalidOperationException>();
        f.Catalogue.BeforeObservation = null;
        await FluentActions.Awaiting(() => f.Service.ResolveTurnAsync(static () => true, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        release.SetResult();
        await refresh;
    }

    [Theory]
    [InlineData("initial")]
    [InlineData("observation")]
    [InlineData("cancel")]
    public async Task TurnObservationRequiresCurrentHostFenceBeforeAndAfterAsynchronousBoundary(string stage)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        bool Eligible()
        {
            calls++;
            if (stage is "cancel" && calls == 2) { cancellation.Cancel(); }
            return stage is not "initial" && (stage is not "observation" || calls != 2);
        }
        await FluentActions.Awaiting(() => f.Service.ResolveTurnAsync(Eligible, cancellation.Token)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task InstallationLossClosesSavedLocalTupleAndMakesResetPreviewStaleWithoutFallback()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        await f.Refresh();
        var tuple = f.TupleChoice();
        f.Catalogue.Installation = LocalProviderInstallation.Unknown;
        await FluentActions.Awaiting(() => f.Apply(tuple)).Should().ThrowAsync<InvalidOperationException>();
        await f.Refresh();
        f.Catalogue.Installation = LocalProviderInstallation.Installed;
        await FluentActions.Awaiting(() => f.Apply(f.Service.CreateResetChoice(tupleOnly: false))).Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Value = tuple.Value;
        f.Catalogue.Installation = LocalProviderInstallation.Missing;
        await f.Refresh();
        f.Service.Get().Effective.Should().BeNull();
        f.Service.Models.Should().BeEmpty();
        f.Service.Get().Desired!.Service.Should().Be(ProviderService.Local);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResetOnlyTheExplicitDraftServiceRetainsOtherTuplesAndRejectsForeignPreview(bool foreign)
    {
        await using var f = new ProviderProfileConfigurationFixture();
        await f.Refresh();
        var choice = f.Service.Choices.Single(item => item.Service == ProviderService.Claude);
        if (foreign)
        {
            var other = f.CreateService();
            await other.RefreshAsync(RequestOrigin.LocalUi, () => f.Eligible, f.Token);
            choice = other.Choices[2];
            FluentActions.Invoking(() => f.Service.CreateResetChoice(tupleOnly: true, choice)).Should().Throw<InvalidOperationException>();
        }
        else
        {
            var reset = f.Service.CreateResetChoice(tupleOnly: true, choice);
            reset.Value.Service.Should().Be(ProviderService.Local);
            await f.Apply(reset);
        }
    }

    [Fact]
    public async Task DraftCatalogueDoesNotAuthorizeForeignUnsetExpiredOrRevokedChoice()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        f.Service.CatalogueFor(new(new(ProviderService.Local, []), 1, Guid.NewGuid(),
            new(new(Guid.NewGuid()), new(1), true), RequestOrigin.LocalUi, static () => true)).Should().BeNull();
        f.Catalogue.Status = ProviderCatalogueStatus.Current;
        await f.Refresh();
        var local = f.Service.Choices[1];
        f.Service.ModelsFor(local).Should().ContainSingle();
        f.Service.ModelsFor(f.Service.Choices[0]).Should().BeEmpty();
        var other = f.CreateService();
        await other.RefreshAsync(RequestOrigin.LocalUi, () => f.Eligible, f.Token);
        f.Service.ModelsFor(other.Choices[1]).Should().BeEmpty();
        f.Catalogue.Installation = LocalProviderInstallation.Unknown;
        await f.Refresh();
        f.Service.ModelsFor(f.Service.Choices[1]).Should().BeEmpty();
        f.Time.Now = f.Catalogue.Expiry;
        f.Service.ModelsFor(f.Service.Choices[3]).Should().BeEmpty();
        f.Eligible = false;
        f.Service.CatalogueFor(f.Service.Choices[1]).Should().BeNull();
    }

    [Fact]
    public async Task BoundedIssuedDraftsRequireExplicitRefreshWithoutEviction()
    {
        await using var f = new ProviderProfileConfigurationFixture();
        await f.Refresh();
        var original = f.Service.Choices[3];
        for (var index = 0; index < 59; index++) { f.Service.CreateResetChoice(tupleOnly: false); }
        FluentActions.Invoking(() => f.Service.CreateResetChoice(tupleOnly: false)).Should().Throw<InvalidOperationException>();
        (await f.Apply(original)).Should().BeTrue();
        f.Preferences.Value!.Service.Should().Be(ProviderService.Claude);
    }
}
