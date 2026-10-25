using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Microsoft.Extensions.DependencyInjection;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteProviderProfileConfigurationTests
{
    [Fact]
    public async Task ProductionRegistrationUsesActualPrivateControlLeaseAndAtomicStoreWithFakeInstallationOnly()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await using var admission = new AudioControlAdmission(f.Store, f.Store, new HostTaskCoordinator(f.Tasks));
        using var policy = new CallCommunicationPolicy(new Call());
        var audit = new Audit();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPreferenceStore>(new LocalPreferenceStore(f.Paths));
        services.AddSingleton(admission);
        services.AddSingleton<ISecurityAuditLog>(audit);
        services.AddSingleton<TimeProvider>(f.Time);
        ProviderProfileComposition.Configure(services);
        services.AddSingleton<ILocalProviderInstallationObservation>(new Installation());
        await using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ProviderProfileConfigurationService>();
        provider.GetRequiredService<ProviderProfileConfigurationService>().Should().BeSameAs(service);
        provider.GetRequiredService<IProviderCapabilityCatalogue>().Should().BeOfType<HostedProviderCatalogue>();
        provider.GetRequiredService<CopilotProviderCatalogue>().Should().NotBeNull();
        provider.GetRequiredService<IProviderProfilePreferences>().Should().BeOfType<LocalProviderProfilePreferences>();
        await service.RefreshAsync(RequestOrigin.LocalUi, static () => true, f.Token);
        service.Get().Desired!.Service.Should().Be(ProviderService.Local);
        service.Models.Should().BeEmpty();
        await service.SelectAsync(service.Choices.Single(choice => choice.Service == ProviderService.Claude),
            policy.Current.Revision, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser, policy, static () => true, f.Token);
        var restarted = new ProviderProfileConfigurationService(new LocalProviderProfilePreferences(f.Paths),
            provider.GetRequiredService<IProviderCapabilityCatalogue>(), admission, audit, f.Time);
        await restarted.RefreshAsync(RequestOrigin.ActivatedVoice, static () => true, f.Token);
        restarted.Get().Saved!.Service.Should().Be(ProviderService.Claude);
        restarted.Get().Effective.Should().BeNull();
        restarted.Get().AuthenticationAvailable.Should().BeFalse();
        await restarted.SelectAsync(restarted.CreateResetChoice(tupleOnly: false), policy.Current.Revision,
            RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand, policy, static () => true, f.Token);
        new LocalProviderProfilePreferences(f.Paths).Load()!.Service.Should().Be(ProviderService.Local);
        audit.Requests.Should().HaveCount(4).And.OnlyContain(request => request != null);
        f.Count("host_questions").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
    }

    private sealed class Installation : ILocalProviderInstallationObservation
    {
        public Task<LocalProviderInstallation> ObserveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(LocalProviderInstallation.Installed);
        }
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal List<HostRequest> Requests { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Requests.Add(HostActivity.RequireCurrent().Request);
    }
    private sealed class Call : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
}
