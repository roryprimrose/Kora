using AwesomeAssertions;
using Kora.Application.ViewModels;
using Kora.Core.Communication;
using Kora.Core.Storage;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Audio;
using Microsoft.Extensions.DependencyInjection;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class DesktopSessionWorkspaceAccessTests
{
    [Fact]
    public void ResolvingWorkspaceAccessDoesNotConstructTheViewModelAndRemainsClosedWithoutBinding()
    {
        var services = new ServiceCollection();
        var ownership = new DesktopInstanceOwnershipBridge();
        services.AddSingleton(ownership);
        services.AddSingleton<MainViewModel>(_ =>
            throw new InvalidOperationException("Workspace access must not construct the view model."));
        services.AddSingleton<DesktopSessionWorkspaceAccess>();
        services.AddSingleton<ISessionWorkspaceAccess>(provider => provider.GetRequiredService<DesktopSessionWorkspaceAccess>());
        using var provider = services.BuildServiceProvider();
        var access = provider.GetRequiredService<ISessionWorkspaceAccess>();
        access.Should().BeSameAs(provider.GetRequiredService<DesktopSessionWorkspaceAccess>());
        access.CanInspect.Should().BeFalse();
        access.CanControl.Should().BeFalse();
        var revision = access.ControlRevision;
        ownership.BindCallbacks(_ => Task.CompletedTask, _ => Task.FromResult(true));
        access.CanInspect.Should().BeFalse();
        access.CanControl.Should().BeFalse();
        access.ControlRevision.Should().BeGreaterThan(revision);
        revision = access.ControlRevision;
        access.ControlRevision.Should().Be(revision);
    }

    [Theory]
    [InlineData(CallState.Clear)]
    [InlineData(CallState.Active)]
    public async Task BindingPreservesOwnershipCallAndPrivacyFencesAndDisposalCannotRestoreAdmission(CallState callState)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var fixture = new NativeUxFixtureSession(parent);
            await fixture.InitializeAsync(null, callState: new FixtureBoundaries.FixtureCallState(callState));
            var ownership = new DesktopInstanceOwnershipBridge();
            using var access = new DesktopSessionWorkspaceAccess(ownership);
            var unboundRevision = access.ControlRevision;
            access.Bind(fixture.Main);
            access.ControlRevision.Should().BeGreaterThan(unboundRevision);
            access.CanInspect.Should().BeFalse();
            access.CanControl.Should().BeFalse();
            access.Invoking(value => value.Bind(fixture.Main)).Should().Throw<InvalidOperationException>();
            ownership.BindCallbacks(_ => Task.CompletedTask, _ => Task.FromResult(true));
            access.CanInspect.Should().BeTrue();
            access.CanControl.Should().BeTrue();
            var admittedRevision = access.ControlRevision;
            access.ControlRevision.Should().Be(admittedRevision);
            ownership.UnbindCallbacks();
            access.CanInspect.Should().BeFalse();
            access.CanControl.Should().BeFalse();
            access.ControlRevision.Should().BeGreaterThan(admittedRevision);
            ownership.BindCallbacks(_ => Task.CompletedTask, _ => Task.FromResult(true));
            access.CanInspect.Should().BeTrue();
            var beforeClosure = access.ControlRevision;
            fixture.Main.CloseForObservedPrivacyEvent("Synthetic workspace privacy closure", hidePresentation: true);
            await fixture.Main.PrivacyClosureTask;
            access.CanInspect.Should().BeFalse();
            access.CanControl.Should().BeFalse();
            access.ControlRevision.Should().BeGreaterThan(beforeClosure);
            var beforeDisposal = access.ControlRevision;
            access.Dispose();
            access.CanInspect.Should().BeFalse();
            access.CanControl.Should().BeFalse();
            access.ControlRevision.Should().BeGreaterThan(beforeDisposal);
            var disposedRevision = access.ControlRevision;
            access.Dispose();
            access.ControlRevision.Should().Be(disposedRevision);
            access.Invoking(value => value.Bind(fixture.Main)).Should().Throw<ObjectDisposedException>();
            fixture.Main.CloseForObservedPrivacyEvent("Synthetic post-disposal closure", hidePresentation: true);
            await fixture.Main.PrivacyClosureTask;
            access.ControlRevision.Should().Be(disposedRevision);
        });
    }

    [Fact]
    public void UnboundAccessRejectsNullBindingAndCanBeDisposedWithoutConstructingPresentation()
    {
        using var access = new DesktopSessionWorkspaceAccess(new DesktopInstanceOwnershipBridge());
        access.Invoking(value => value.Bind(null!)).Should().Throw<ArgumentNullException>();
        access.CanInspect.Should().BeFalse();
        access.CanControl.Should().BeFalse();
        var beforeDisposal = access.ControlRevision;
        access.Dispose();
        access.ControlRevision.Should().BeGreaterThan(beforeDisposal);
        access.CanInspect.Should().BeFalse();
        access.CanControl.Should().BeFalse();
    }
}
