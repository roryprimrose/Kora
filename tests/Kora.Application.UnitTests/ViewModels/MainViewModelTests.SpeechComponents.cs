using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Core.Communication;
using Kora.Testing;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task SpeechComponentSettingsRequiresLiveOriginalNativeOwningHost()
    {
        var component = new SpeechComponentSetupFixture();
        var fixture = new Fixture(speechComponentSetup: component.Service);
        fixture.ViewModel.SpeechComponentOffers.Should().ContainSingle();
        fixture.ViewModel.SpeechComponentInspectionTargets.Should().ContainSingle();
        await FluentActions.Awaiting(() => fixture.ViewModel.InspectSpeechComponentAsync(component.Component.Identity,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        await FluentActions.Awaiting(() => fixture.ViewModel.ReviewSpeechComponentAsync(component.Component.Identity,
            SpeechComponentOperation.Prepare, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        fixture.ViewModel.BindSpeechComponentNativeLifetime(() => true);
        var plan = await fixture.ViewModel.ReviewSpeechComponentAsync(component.Component.Identity,
            SpeechComponentOperation.Prepare, TestContext.Current.CancellationToken);
        fixture.ViewModel.BindSpeechComponentNativeLifetime(() => true);
        await FluentActions.Awaiting(() => fixture.ViewModel.ApplySpeechComponentAsync(plan, true, component.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<UnauthorizedAccessException>();
        component.Store.Prepares.Should().Be(0);
    }

    [Fact]
    public async Task SpeechComponentSettingsCompletesSharedOperationWithoutSelectingVoiceOrOpeningMicrophone()
    {
        var component = new SpeechComponentSetupFixture();
        var fixture = new Fixture(speechComponentSetup: component.Service);
        fixture.ViewModel.BindSpeechComponentNativeLifetime(() => true);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var selectedOutput = fixture.ViewModel.SelectedSpeechProvider;
        var wasListening = fixture.ViewModel.IsListening;
        var wasVoiceEnabled = fixture.ViewModel.IsVoiceEnabled;
        var before = fixture.Events.ToArray();
        var observed = await fixture.ViewModel.InspectSpeechComponentAsync(component.Component.Identity, TestContext.Current.CancellationToken);
        observed.Storage?.Installed.Should().BeFalse();
        var plan = await fixture.ViewModel.ReviewSpeechComponentAsync(component.Component.Identity,
            SpeechComponentOperation.Prepare, TestContext.Current.CancellationToken);
        var result = await fixture.ViewModel.ApplySpeechComponentAsync(plan, true, component.Progress, TestContext.Current.CancellationToken);
        result.Runtime.Functional.Should().Be(Kora.Core.Dependencies.SpeechComponentFunctionalEvidence.Verified);
        fixture.ViewModel.SelectedSpeechProvider.Should().BeSameAs(selectedOutput);
        fixture.ViewModel.IsListening.Should().Be(wasListening);
        fixture.ViewModel.IsVoiceEnabled.Should().Be(wasVoiceEnabled);
        fixture.Events.Should().Equal(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpeechComponentSettingsRejectsUnavailableServiceOrLockedHost(bool withService)
    {
        var component = new SpeechComponentSetupFixture();
        var fixture = new Fixture(speechComponentSetup: withService ? component.Service : null);
        fixture.ViewModel.IsSpeechComponentSetupAvailable.Should().Be(withService);
        fixture.ViewModel.BindSpeechComponentNativeLifetime(() => true);
        fixture.Session.IsUnlocked = false;
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        await FluentActions.Awaiting(() => fixture.ViewModel.InspectSpeechComponentAsync(component.Component.Identity,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        component.Store.Inspections.Should().Be(0);
        if (!withService)
        {
            fixture.ViewModel.SpeechComponentOffers.Should().BeEmpty();
            fixture.ViewModel.SpeechComponentInspectionTargets.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpeechComponentSettingsCannotRelabelMissingOrActivatedVoiceContextAsLocalUi(bool activatedVoice)
    {
        var component = new SpeechComponentSetupFixture();
        var fixture = new Fixture(speechComponentSetup: component.Service);
        fixture.ViewModel.BindSpeechComponentNativeLifetime(() => true);
        using var host = activatedVoice
            ? HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Application, HostOperation.Request)
            : null;
        await FluentActions.Awaiting(() => fixture.ViewModel.InspectSpeechComponentAsync(component.Component.Identity,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        component.Store.Inspections.Should().Be(0);
    }

    [Fact]
    public async Task SpeechComponentSettingsCannotSupersedeAnExistingApproval()
    {
        var component = new SpeechComponentSetupFixture();
        var fixture = new Fixture(speechComponentSetup: component.Service);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindSpeechComponentNativeLifetime(() => true);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        await fixture.ViewModel.PrepareGrantChangeAsync(new Kora.Core.Commands.GrantChange(
            Kora.Core.Commands.GrantChangeOperation.Add, Kora.Core.Commands.BuiltInAction.LockMachine,
            Kora.Core.Commands.ModelApprovalScope.Always));
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        await FluentActions.Awaiting(() => fixture.ViewModel.InspectSpeechComponentAsync(component.Component.Identity,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        component.Store.Inspections.Should().Be(0);
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
    }

    [Fact]
    public async Task SpeechComponentSettingsLifetimeRevocationWithSameBindingInvalidatesPendingReview()
    {
        var component = new SpeechComponentSetupFixture();
        var fixture = new Fixture(speechComponentSetup: component.Service);
        var visible = true;
        fixture.ViewModel.BindSpeechComponentNativeLifetime(() => visible);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var plan = await fixture.ViewModel.ReviewSpeechComponentAsync(component.Component.Identity,
            SpeechComponentOperation.Prepare, TestContext.Current.CancellationToken);
        component.Store.AfterInspect = () => visible = false;
        await FluentActions.Awaiting(() => fixture.ViewModel.ApplySpeechComponentAsync(plan, true, component.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        component.Store.Prepares.Should().Be(0);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("resource")]
    [InlineData("residency")]
    [InlineData("saved-state")]
    [InlineData("private")]
    [InlineData("call")]
    [InlineData("owner")]
    [InlineData("consent")]
    public async Task SpeechRemovalReviewCannotSurviveSelectionPrivacyConsentOrCallDrift(string change)
    {
        var component = new SpeechComponentSetupFixture();
        component.Store.State = new(true, true, SpeechComponentOwnership.Managed, "verified")
        { ExactBytes = 72, Inventory = "Exact fixture-owned model, tokens and receipt." };
        var fixture = new Fixture(speechComponentSetup: component.Service, enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        fixture.VoiceConsent.Consent = true;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindSpeechComponentNativeLifetime(() => true);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var plan = await fixture.ViewModel.ReviewSpeechComponentAsync(component.Component.Identity,
            SpeechComponentOperation.Remove, TestContext.Current.CancellationToken);
        switch (change)
        {
            case "profile": fixture.ViewModel.SelectedSpeechInputProfile = null; break;
            case "resource": fixture.ViewModel.SelectedSpeechInputResource = SpeechInputResource.Cpu; break;
            case "residency": fixture.ViewModel.SelectedSpeechInputResidency = SpeechInputResidency.KeepLoaded; break;
            case "saved-state": fixture.InputRuntime.InitializeProfile(null); break;
            case "private": fixture.Session.IsUnlocked = false; break;
            case "call": fixture.CallState.SetState(CallState.Active); break;
            case "owner": fixture.ViewModel.BindCallOwnershipGate(() => false); break;
            case "consent": await fixture.ViewModel.WithdrawVoiceConsentCommand.ExecuteAsync(); break;
        }
        await FluentActions.Awaiting(() => fixture.ViewModel.ApplySpeechComponentAsync(plan, true, component.Progress,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<Exception>();
        component.Store.Removals.Should().Be(0);
        component.RuntimeAdapter.Leases.Should().Be(0);
        fixture.InputPreferences.Value.Should().BeNull();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task ConfirmedNativeRemovalPreservesSavedChoiceAndAllConsentWithoutFallbackOrAcquisition()
    {
        var component = new SpeechComponentSetupFixture();
        component.Store.State = new(true, true, SpeechComponentOwnership.Managed, "verified")
        { ExactBytes = 72, Inventory = "Exact fixture inventory." };
        var fixture = new Fixture(speechComponentSetup: component.Service, enableSpeechInput: true);
        await using var configuration = fixture.InputConfiguration!;
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.InputRuntime.InitializeProfile(null);
        var before = fixture.ViewModel.SpeechInputStatus;
        var voiceConsent = fixture.ViewModel.HasVoiceConsent;
        fixture.ViewModel.BindSpeechComponentNativeLifetime(() => true);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var plan = await fixture.ViewModel.ReviewSpeechComponentAsync(component.Component.Identity,
            SpeechComponentOperation.Remove, TestContext.Current.CancellationToken);
        var result = await fixture.ViewModel.ApplySpeechComponentAsync(plan, true, component.Progress, TestContext.Current.CancellationToken);
        result.Storage!.Ownership.Should().Be(SpeechComponentOwnership.Missing);
        fixture.ViewModel.SpeechInputStatus.Should().Be(before).And.Contain("unavailable");
        fixture.ViewModel.HasVoiceConsent.Should().Be(voiceConsent);
        fixture.InputPreferences.Value.Should().BeNull();
        component.Store.Prepares.Should().Be(0);
        fixture.Voice.StartCalls.Should().Be(0);
        fixture.ViewModel.Dispose();
    }
}