using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
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
        if (!withService) { fixture.ViewModel.SpeechComponentOffers.Should().BeEmpty(); }
    }
}
